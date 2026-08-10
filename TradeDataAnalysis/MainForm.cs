using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace TradeDataAnalysis
{
    public partial class MainForm : Form
    {
        private List<TradeBar> _allBars = new List<TradeBar>();
        private List<Trade> _allTrades = new List<Trade>();
        private List<SavedQuery> _savedQueries = new List<SavedQuery>();
        private readonly string _presetFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_queries.json");

        // UI Controls
        private TextBox txtDirectoryPath;
        private Button btnBrowse;
        private Button btnLoadData;
        private ComboBox cboQueryPresets;
        private Button btnSaveQuery;
        private Button btnDeleteQuery;
        private TextBox txtLinqQuery;
        private Button btnRunQuery;
        private Button btnExportCsv;
        private DataGridView gridResults;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        public MainForm()
        {
            InitializeComponent();
            InitializeComponentUI();
            LoadDefaultAndUserPresets();
        }

        private void InitializeComponentUI()
        {
            this.Text = "AnalyzeTradeData - WinForms LINQ Analyzer";
            this.Size = new Size(1250, 850);

            // --- Top Panel: Directory Loader ---
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };
            txtDirectoryPath = new TextBox { Left = 12, Top = 12, Width = 600 };
            btnBrowse = new Button { Text = "Browse...", Left = 620, Top = 10, Width = 80 };
            btnLoadData = new Button { Text = "Load Data", Left = 710, Top = 10, Width = 100 };

            btnBrowse.Click += BtnBrowse_Click;
            btnLoadData.Click += BtnLoadData_Click;
            pnlTop.Controls.AddRange(new Control[] { txtDirectoryPath, btnBrowse, btnLoadData });

            // --- Presets Panel ---
            var pnlPresets = new Panel { Dock = DockStyle.Top, Height = 40 };
            var lblPresets = new Label { Text = "Query Presets:", Left = 12, Top = 10, AutoSize = true };
            cboQueryPresets = new ComboBox { Left = 105, Top = 6, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            btnSaveQuery = new Button { Text = "Save As Preset...", Left = 512, Top = 5, Width = 120 };
            btnDeleteQuery = new Button { Text = "Delete Preset", Left = 638, Top = 5, Width = 100 };
            btnExportCsv = new Button { Text = "Export Grid to CSV", Left = 744, Top = 5, Width = 130 };

            cboQueryPresets.SelectedIndexChanged += CboQueryPresets_SelectedIndexChanged;
            btnSaveQuery.Click += BtnSaveQuery_Click;
            btnDeleteQuery.Click += BtnDeleteQuery_Click;
            btnExportCsv.Click += BtnExportCsv_Click;

            pnlPresets.Controls.AddRange(new Control[] { lblPresets, cboQueryPresets, btnSaveQuery, btnDeleteQuery, btnExportCsv });

            // --- Query Editor Panel ---
            var pnlQuery = new Panel { Dock = DockStyle.Top, Height = 130 };
            var lblQuery = new Label { Text = "LINQ Query (Available variables: 'Bars', 'Trades'):", Left = 12, Top = 4, AutoSize = true };

            txtLinqQuery = new TextBox
            {
                Left = 12,
                Top = 22,
                Width = 1050,
                Height = 90,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F)
            };

            btnRunQuery = new Button { Text = "Run Query", Left = 1070, Top = 22, Width = 140, Height = 90, Font = new Font(this.Font.FontFamily, 10F, FontStyle.Bold) };
            btnRunQuery.Click += BtnRunQuery_Click;

            pnlQuery.Controls.AddRange(new Control[] { lblQuery, txtLinqQuery, btnRunQuery });

            // --- Results Grid ---
            gridResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };

            // --- Status Bar ---
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Ready. Select directory and load data." };
            statusStrip.Items.Add(lblStatus);

            this.Controls.Add(gridResults);
            this.Controls.Add(pnlQuery);
            this.Controls.Add(pnlPresets);
            this.Controls.Add(pnlTop);
            this.Controls.Add(statusStrip);
        }

        #region Preset Management

        private void LoadDefaultAndUserPresets()
        {
            _savedQueries = new List<SavedQuery>
            {
                new SavedQuery
                {
                    Name = "[Built-In] Evaluate Early Cut Rule (-0.5 R at Bar 2)",
                    QueryText = @"Trades.Select(t => {
    var cutBar = t.Bars.FirstOrDefault(b => b.BarsSinceEntry >= 2 && b.CurrentR < -0.5);
    return new {
        t.TradeId,
        OriginalFinalR = t.FinalR,
        CutTriggered = cutBar != null,
        CutBarNumber = cutBar?.BarsSinceEntry,
        SimulatedR = cutBar != null ? cutBar.CurrentR : t.FinalR,
        SavedR = cutBar != null ? cutBar.CurrentR - t.FinalR : 0.0
    };
})"
                },
                new SavedQuery
                {
                    Name = "[Built-In] System-Wide Baseline vs Cut Comparison",
                    QueryText = @"new[] {
    new { 
        Strategy = ""Original Baseline"", 
        TotalR = Trades.Sum(t => t.FinalR),
        WinRatePct = Trades.Count(t => t.IsWinner) * 100.0 / Trades.Count
    },
    new { 
        Strategy = ""With Early Cut Rule"", 
        TotalR = Trades.Sum(t => {
            var cut = t.Bars.FirstOrDefault(b => b.BarsSinceEntry >= 2 && b.CurrentR <= -0.5 && b.EMASpreadSlope5 < 0);
            return cut != null ? cut.CurrentR : t.FinalR;
        }),
        WinRatePct = Trades.Count(t => {
            var cut = t.Bars.FirstOrDefault(b => b.BarsSinceEntry >= 2 && b.CurrentR <= -0.5 && b.EMASpreadSlope5 < 0);
            double r = cut != null ? cut.CurrentR : t.FinalR;
            return r > 0;
        }) * 100.0 / Trades.Count
    }
}"
                },
                new SavedQuery
                {
                    Name = "[Built-In] Find Recovered Losers (MAE < -0.5 R but hit Profit)",
                    QueryText = @"Trades.Where(t => t.Bars.Any(b => b.MAE <= -0.5) && t.FinalR > 0)
      .Select(t => new {
          t.TradeId,
          MinMAE = t.Bars.Min(b => b.MAE),
          MaxMFE = t.Bars.Max(b => b.MFE),
          FinalR = t.FinalR,
          TotalBars = t.Bars.Count
      })"
                }
            };

            // Load custom user queries from disk if present
            if (File.Exists(_presetFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_presetFilePath);
                    var userPresets = JsonSerializer.Deserialize<List<SavedQuery>>(json);
                    if (userPresets != null)
                    {
                        _savedQueries.AddRange(userPresets);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not load saved queries file: {ex.Message}", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            RefreshPresetDropdown();
        }

        private void RefreshPresetDropdown()
        {
            cboQueryPresets.DataSource = null;
            cboQueryPresets.DataSource = _savedQueries;
            cboQueryPresets.DisplayMember = "Name";
            if (_savedQueries.Count > 0)
            {
                cboQueryPresets.SelectedIndex = 0;
            }
        }

        private void SaveUserPresetsToDisk()
        {
            try
            {
                var customOnly = _savedQueries.Where(q => !q.Name.StartsWith("[Built-In]")).ToList();
                string json = JsonSerializer.Serialize(customOnly, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_presetFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save query preset to disk: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CboQueryPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboQueryPresets.SelectedItem is SavedQuery selected)
            {
                txtLinqQuery.Text = selected.QueryText;
                btnDeleteQuery.Enabled = !selected.Name.StartsWith("[Built-In]");
            }
        }

        private void BtnSaveQuery_Click(object sender, EventArgs e)
        {
            string queryText = txtLinqQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(queryText))
            {
                MessageBox.Show("Please write a query before saving.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var prompt = new PromptForm("Save Query Preset", "Enter a name for this preset query:"))
            {
                if (prompt.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(prompt.InputText))
                {
                    string presetName = prompt.InputText.Trim();

                    var existing = _savedQueries.FirstOrDefault(q => q.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        if (existing.Name.StartsWith("[Built-In]"))
                        {
                            MessageBox.Show("Cannot overwrite built-in presets.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        existing.QueryText = queryText;
                    }
                    else
                    {
                        var newQuery = new SavedQuery { Name = presetName, QueryText = queryText };
                        _savedQueries.Add(newQuery);
                    }

                    SaveUserPresetsToDisk();
                    RefreshPresetDropdown();
                    cboQueryPresets.SelectedIndex = _savedQueries.FindIndex(q => q.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
                    MessageBox.Show("Preset saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnDeleteQuery_Click(object sender, EventArgs e)
        {
            if (cboQueryPresets.SelectedItem is SavedQuery selected)
            {
                if (selected.Name.StartsWith("[Built-In]")) return;

                var result = MessageBox.Show($"Delete preset '{selected.Name}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    _savedQueries.Remove(selected);
                    SaveUserPresetsToDisk();
                    RefreshPresetDropdown();
                }
            }
        }

        #endregion

        #region Data & Query Execution
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtDirectoryPath.Text = dlg.SelectedPath;
                }
            }
        }

        private async void BtnLoadData_Click(object sender, EventArgs e)
        {
            string dirPath = txtDirectoryPath.Text.Trim();
            if (!Directory.Exists(dirPath))
            {
                MessageBox.Show("Please select a valid directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnLoadData.Enabled = false;
            lblStatus.Text = "Loading CSV files...";

            await Task.Run(() => LoadCsvData(dirPath));

            lblStatus.Text = $"Loaded {_allBars.Count:N0} bars across {_allTrades.Count:N0} unique trades.";
            btnLoadData.Enabled = true;
        }

        private void LoadCsvData(string dirPath)
        {
            var files = Directory.GetFiles(dirPath, "*.csv", SearchOption.AllDirectories);
            var loadedBars = new List<TradeBar>();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null
            };

            foreach (var file in files)
            {
                try
                {
                    using (var reader = new StreamReader(file))
                    using (var csv = new CsvReader(reader, config))
                    {
                        var records = csv.GetRecords<TradeBar>();
                        loadedBars.AddRange(records);
                    }
                }
                catch
                {
                    // Ignore corrupt or non-matching CSV files
                }
            }

            _allBars = loadedBars;
            _allTrades = _allBars
                .GroupBy(b => b.TradeId)
                .Select(g => new Trade { TradeId = g.Key, Bars = g.OrderBy(b => b.BarsSinceEntry).ToList() })
                .ToList();
        }

        private async void BtnRunQuery_Click(object sender, EventArgs e)
        {
            if (_allBars.Count == 0)
            {
                MessageBox.Show("Please load trade data first.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string userCode = txtLinqQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(userCode)) return;

            btnRunQuery.Enabled = false;
            lblStatus.Text = "Executing LINQ query...";

            try
            {
                var globals = new ScriptGlobals { Bars = _allBars, Trades = _allTrades };

                var scriptOptions = ScriptOptions.Default
                    .WithReferences(typeof(Enumerable).Assembly, typeof(TradeBar).Assembly)
                    .WithImports("System", "System.Linq", "System.Collections.Generic");

                object queryResult = await CSharpScript.EvaluateAsync(userCode, scriptOptions, globals);

                if (queryResult is IEnumerable enumerable)
                {
                    var resultList = enumerable.Cast<object>().ToList();
                    gridResults.DataSource = resultList;
                    lblStatus.Text = $"Query completed. {resultList.Count:N0} rows returned.";
                }
                else
                {
                    gridResults.DataSource = new List<object> { new { Result = queryResult } };
                    lblStatus.Text = "Query completed (scalar result).";
                }
            }
            catch (CompilationErrorException ex)
            {
                MessageBox.Show($"Compilation Error:\n\n{string.Join("\n", ex.Diagnostics)}", "Script Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Query failed due to compilation errors.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution Error:\n\n{ex.Message}", "Script Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Query failed during execution.";
            }
            finally
            {
                btnRunQuery.Enabled = true;
            }
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            if (gridResults.Rows.Count == 0)
            {
                MessageBox.Show("No results to export.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var sfd = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv", FileName = "query_results.csv" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    using (var writer = new StreamWriter(sfd.FileName))
                    {
                        var headers = gridResults.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText);
                        writer.WriteLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

                        foreach (DataGridViewRow row in gridResults.Rows)
                        {
                            if (row.IsNewRow) continue;
                            var cells = row.Cells.Cast<DataGridViewCell>().Select(c => $"\"{c.Value?.ToString() ?? ""}\"");
                            writer.WriteLine(string.Join(",", cells));
                        }
                    }
                    MessageBox.Show("Results exported successfully!", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        #endregion
    }
}


/*




using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace TradeDataAnalysis // Match your project's namespace
{
    public partial class MainForm : Form
    {
        private List<TradeBar> _allBars = new List<TradeBar>();
        private List<Trade> _allTrades = new List<Trade>();
        private List<SavedQuery> _savedQueries = new List<SavedQuery>();
        private readonly string _presetFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_queries.json");

        // UI Controls
        private TextBox txtDirectoryPath;
        private Button btnBrowse;
        private Button btnLoadData;
        private ComboBox cboQueryPresets;
        private Button btnSaveQuery;
        private Button btnDeleteQuery;
        private TextBox txtLinqQuery;
        private Button btnRunQuery;
        private Button btnExportCsv;
        private DataGridView gridResults;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        public MainForm()
        {
            InitializeComponent(); // Keeps VS designer initialization
            InitializeComponentUI(); // Builds our custom layout
            LoadDefaultAndUserPresets();
        }

        private void InitializeComponentUI()
        {
            this.Text = "Trade Data LINQ Analyzer";
            this.Size = new Size(1250, 850);

            // --- Top Panel: Directory Loader ---
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };
            txtDirectoryPath = new TextBox { Left = 12, Top = 12, Width = 600 };
            btnBrowse = new Button { Text = "Browse...", Left = 620, Top = 10, Width = 80 };
            btnLoadData = new Button { Text = "Load Data", Left = 710, Top = 10, Width = 100 };

            btnBrowse.Click += BtnBrowse_Click;
            btnLoadData.Click += BtnLoadData_Click;
            pnlTop.Controls.AddRange(new Control[] { txtDirectoryPath, btnBrowse, btnLoadData });

            // --- Presets Panel ---
            var pnlPresets = new Panel { Dock = DockStyle.Top, Height = 40 };
            var lblPresets = new Label { Text = "Query Presets:", Left = 12, Top = 10, AutoSize = true };
            cboQueryPresets = new ComboBox { Left = 105, Top = 6, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            btnSaveQuery = new Button { Text = "Save As Preset...", Left = 512, Top = 5, Width = 120 };
            btnDeleteQuery = new Button { Text = "Delete Preset", Left = 638, Top = 5, Width = 100 };
            btnExportCsv = new Button { Text = "Export Grid to CSV", Left = 744, Top = 5, Width = 130 };

            cboQueryPresets.SelectedIndexChanged += CboQueryPresets_SelectedIndexChanged;
            btnSaveQuery.Click += BtnSaveQuery_Click;
            btnDeleteQuery.Click += BtnDeleteQuery_Click;
            btnExportCsv.Click += BtnExportCsv_Click;

            pnlPresets.Controls.AddRange(new Control[] { lblPresets, cboQueryPresets, btnSaveQuery, btnDeleteQuery, btnExportCsv });

            // --- Query Editor Panel ---
            var pnlQuery = new Panel { Dock = DockStyle.Top, Height = 130 };
            var lblQuery = new Label { Text = "LINQ Query (Available variables: 'Bars', 'Trades'):", Left = 12, Top = 4, AutoSize = true };

            txtLinqQuery = new TextBox
            {
                Left = 12,
                Top = 22,
                Width = 1050,
                Height = 90,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F)
            };

            btnRunQuery = new Button { Text = "Run Query", Left = 1070, Top = 22, Width = 140, Height = 90, Font = new Font(this.Font.FontFamily, 10F, FontStyle.Bold) };
            btnRunQuery.Click += BtnRunQuery_Click;

            pnlQuery.Controls.AddRange(new Control[] { lblQuery, txtLinqQuery, btnRunQuery });

            // --- Results Grid ---
            gridResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };

            // --- Status Bar ---
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Ready. Select directory and load data." };
            statusStrip.Items.Add(lblStatus);

            this.Controls.Add(gridResults);
            this.Controls.Add(pnlQuery);
            this.Controls.Add(pnlPresets);
            this.Controls.Add(pnlTop);
            this.Controls.Add(statusStrip);
        }

        // ... [Paste remaining methods: BtnBrowse_Click, LoadCsvData, BtnRunQuery_Click, etc.] ...
    }
}
*/