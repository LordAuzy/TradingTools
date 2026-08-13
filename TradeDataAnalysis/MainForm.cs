using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CsvHelper;
using CsvHelper.Configuration;
using DuckDB.NET.Data;

namespace TradeDataAnalysis
{
    public partial class MainForm : Form
    {
        private readonly TradeDataService _dataService = new TradeDataService();
        private List<TradeBar> _allBars = new List<TradeBar>();
        private List<Trade> _allTrades = new List<Trade>();
        private List<SavedQuery> _savedQueries = new List<SavedQuery>();
        private readonly string _presetFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_queries.json");
        private readonly string _layoutFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TradingTools",
            "TradeDataAnalysis",
            "window-layout.json");

        private sealed class WindowLayoutState
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public FormWindowState WindowState { get; set; }
            public int SplitterDistance { get; set; }

            public string? DirectoryPath { get; set; }
            public int QueryPresetSelectedIndex { get; set; } = -1;
        }

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
        private SplitContainer splitContainer1;

        public MainForm()
        {
            InitializeComponent();
            InitializeComponentUI();
            LoadDefaultAndUserPresets();
            LoadWindowLayout();
            FormClosing += (_, _) => SaveWindowLayout();
        }

        private void SaveWindowLayout()
        {
            try
            {
                Rectangle bounds = WindowState == FormWindowState.Normal
                    ? Bounds
                    : RestoreBounds;

                var state = new WindowLayoutState
                {
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    WindowState = WindowState == FormWindowState.Minimized
                        ? FormWindowState.Normal
                        : WindowState,
                    SplitterDistance = splitContainer1.SplitterDistance,

                    DirectoryPath = txtDirectoryPath.Text,
                    QueryPresetSelectedIndex = cboQueryPresets.SelectedIndex
                };

                Directory.CreateDirectory(Path.GetDirectoryName(_layoutFilePath)!);

                File.WriteAllText(
                    _layoutFilePath,
                    JsonSerializer.Serialize(state, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch
            {
                // Ignore layout save errors.
            }
        }

        private void LoadWindowLayout()
        {
            try
            {
                if (!File.Exists(_layoutFilePath))
                    return;

                var json = File.ReadAllText(_layoutFilePath);
                var state = JsonSerializer.Deserialize<WindowLayoutState>(json);

                if (state == null)
                    return;

                txtDirectoryPath.Text = state.DirectoryPath ?? string.Empty;

                if (state.QueryPresetSelectedIndex >= 0 &&
                    state.QueryPresetSelectedIndex < cboQueryPresets.Items.Count)
                {
                    cboQueryPresets.SelectedIndex = state.QueryPresetSelectedIndex;
                }

                var bounds = new Rectangle(state.X, state.Y, state.Width, state.Height);

                if (IsVisibleOnAnyScreen(bounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = bounds;
                }

                Shown += (_, _) =>
                {
                    RestoreSplitterDistance(state.SplitterDistance);

                    if (state.WindowState == FormWindowState.Maximized)
                        WindowState = FormWindowState.Maximized;
                };
            }
            catch
            {
                // Ignore layout restore errors.
            }
        }

        private static bool IsVisibleOnAnyScreen(Rectangle bounds)
        {
            return Screen.AllScreens.Any(screen =>
                screen.WorkingArea.IntersectsWith(bounds));
        }

        private void RestoreSplitterDistance(int splitterDistance)
        {
            int min = splitContainer1.Panel1MinSize;
            int max;

            if (splitContainer1.Orientation == Orientation.Vertical)
            {
                max = splitContainer1.Width
                    - splitContainer1.Panel2MinSize
                    - splitContainer1.SplitterWidth;
            }
            else
            {
                max = splitContainer1.Height
                    - splitContainer1.Panel2MinSize
                    - splitContainer1.SplitterWidth;
            }

            if (max <= min)
                return;

            splitContainer1.SplitterDistance = Math.Clamp(splitterDistance, min, max);
        }

        private void InitializeComponentUI()
        {
            this.Text = "AnalyzeTradeData - WinForms LINQ Analyzer";
            this.Size = new Size(1250, 850);
            this.MinimumSize = new Size(900, 600);

            // --- Main Vertical SplitContainer (Top Controls vs DataGridView) ---
            splitContainer1 = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 240, // Initial height of the top section
                SplitterWidth = 6,      // Width of the draggable divider bar
                Panel1MinSize = 150,    // Prevents resizing top panel too small
                Panel2MinSize = 100     // Prevents resizing grid area too small
            };

            // --- 1. Top Bar: Directory Loader (FlowLayoutPanel) ---
            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Padding = new Padding(10, 10, 10, 5)
            };

            txtDirectoryPath = new TextBox { Width = 700, Margin = new Padding(0, 3, 8, 0) };
            btnBrowse = new Button { Text = "Browse...", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0) };
            btnLoadData = new Button { Text = "Load Data", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };

            btnBrowse.Click += BtnBrowse_Click;
            btnLoadData.Click += BtnLoadData_Click;
            pnlTop.Controls.AddRange(new Control[] { txtDirectoryPath, btnBrowse, btnLoadData });

            // --- 2. Presets Bar (FlowLayoutPanel) ---
            var pnlPresets = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Padding = new Padding(10, 5, 10, 5)
            };

            var lblPresets = new Label { Text = "Query Presets:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            cboQueryPresets = new ComboBox { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 12, 0) };

            btnSaveQuery = new Button { Text = "Save As Preset...", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0) };
            btnDeleteQuery = new Button { Text = "Delete Preset", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0) };
            btnExportCsv = new Button { Text = "Export Grid to CSV", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

            cboQueryPresets.SelectedIndexChanged += CboQueryPresets_SelectedIndexChanged;
            btnSaveQuery.Click += BtnSaveQuery_Click;
            btnDeleteQuery.Click += BtnDeleteQuery_Click;
            btnExportCsv.Click += BtnExportCsv_Click;

            pnlPresets.Controls.AddRange(new Control[] { lblPresets, cboQueryPresets, btnSaveQuery, btnDeleteQuery, btnExportCsv });

            // --- 3. Query Editor Area (TableLayoutPanel - Expands Vertically with Splitter) ---
            var pnlQuery = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, // Fills remaining vertical space in Panel1
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10, 5, 10, 10)
            };

            pnlQuery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlQuery.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pnlQuery.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Textbox expands as panel resizes
            pnlQuery.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblQuery = new Label { Text = "SQL Query (Available tables: 'Bars', 'Trades'):", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };

            txtLinqQuery = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F),
                Margin = new Padding(0, 0, 10, 0)
            };

            btnRunQuery = new Button
            {
                Text = "Run Query",
                Dock = DockStyle.Left,
                Width = 130,
                Height = 40,
                MinimumSize = new Size(130, 50),
                Padding = new Padding(8, 6, 8, 6),
                Font = new Font(this.Font.FontFamily, 10F, FontStyle.Bold)
            };
            btnRunQuery.Click += BtnRunQuery_Click;

            pnlQuery.Controls.Add(lblQuery, 0, 0);
            pnlQuery.SetColumnSpan(lblQuery, 2);
            pnlQuery.Controls.Add(txtLinqQuery, 0, 1);
            pnlQuery.Controls.Add(btnRunQuery, 0, 2);

            // Assembly of Panel 1 (Top Controls + Query Editor)
            splitContainer1.Panel1.Controls.Add(pnlQuery);
            splitContainer1.Panel1.Controls.Add(pnlPresets);
            splitContainer1.Panel1.Controls.Add(pnlTop);

            // --- 4. Results DataGridView (Fills Panel 2) ---
            gridResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };
            splitContainer1.Panel2.Controls.Add(gridResults);

            // --- 5. Status Strip ---
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Ready. Select directory and load data." };
            statusStrip.Items.Add(lblStatus);

            // Add root components to form
            this.Controls.Add(splitContainer1);
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
                    QueryText = @"WITH CutBars AS (
    SELECT
        TradeId,
        MIN(BarsSinceEntry) AS CutBarNumber
    FROM Bars
    WHERE BarsSinceEntry >= 2 AND CurrentR < -0.5
    GROUP BY TradeId
)
SELECT
    t.TradeId,
    t.FinalR AS OriginalFinalR,
    c.CutBarNumber IS NOT NULL AS CutTriggered,
    c.CutBarNumber,
    COALESCE(cb.CurrentR, t.FinalR) AS SimulatedR,
    COALESCE(cb.CurrentR, t.FinalR) - t.FinalR AS SavedR
FROM Trades t
LEFT JOIN CutBars c ON c.TradeId = t.TradeId
LEFT JOIN Bars cb ON cb.TradeId = c.TradeId AND cb.BarsSinceEntry = c.CutBarNumber
ORDER BY t.TradeId"
                },
                new SavedQuery
                {
                    Name = "[Built-In] System-Wide Baseline vs Cut Comparison",
                    QueryText = @"WITH CutBars AS (
    SELECT
        TradeId,
        MIN(BarsSinceEntry) AS CutBarNumber
    FROM Bars
    WHERE BarsSinceEntry >= 2 AND CurrentR <= -0.5 AND EMASpreadSlope5 < 0
    GROUP BY TradeId
),
SimulatedTrades AS (
    SELECT
        t.TradeId,
        t.FinalR,
        COALESCE(cb.CurrentR, t.FinalR) AS SimulatedR
    FROM Trades t
    LEFT JOIN CutBars c ON c.TradeId = t.TradeId
    LEFT JOIN Bars cb ON cb.TradeId = c.TradeId AND cb.BarsSinceEntry = c.CutBarNumber
)
SELECT 'Original Baseline' AS Strategy,
       SUM(FinalR) AS TotalR,
       COUNT(*) FILTER (WHERE FinalR > 0) * 100.0 / COUNT(*) AS WinRatePct
FROM SimulatedTrades
UNION ALL
SELECT 'With Early Cut Rule' AS Strategy,
       SUM(SimulatedR) AS TotalR,
       COUNT(*) FILTER (WHERE SimulatedR > 0) * 100.0 / COUNT(*) AS WinRatePct
FROM SimulatedTrades"
                },
                new SavedQuery
                {
                    Name = "[Built-In] Find Recovered Losers (MAE < -0.5 R but hit Profit)",
                    QueryText = @"SELECT
    t.TradeId,
    MIN(b.MAE) AS MinMAE,
    MAX(b.MFE) AS MaxMFE,
    t.FinalR,
    t.BarCount AS TotalBars
FROM Trades t
JOIN Bars b ON b.TradeId = t.TradeId
GROUP BY t.TradeId, t.FinalR, t.BarCount
HAVING MIN(b.MAE) <= -0.5 AND t.FinalR > 0
ORDER BY t.TradeId"
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

            try
            {
                // Execute non-UI loading on background thread
                var (totalBars, totalTrades) = await Task.Run(() => _dataService.LoadCsvData(dirPath));
                lblStatus.Text = $"Loaded {totalBars:N0} bars across {totalTrades:N0} unique trades.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Loading Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Data load failed.";
            }
            finally
            {
                btnLoadData.Enabled = true;
            }
        }

        private async void BtnRunQuery_Click(object sender, EventArgs e)
        {
            if (!_dataService.HasData)
            {
                MessageBox.Show("Please load trade data first.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string userSql = txtLinqQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(userSql)) return;

            btnRunQuery.Enabled = false;
            lblStatus.Text = "Executing SQL query...";

            try
            {
                DataTable resultTable = await Task.Run(() => RunDuckDbQuery(userSql));

                gridResults.DataSource = resultTable;

                foreach (DataGridViewColumn column in gridResults.Columns)
                {
                    column.SortMode = DataGridViewColumnSortMode.Automatic;
                }

                lblStatus.Text = $"Query completed. {resultTable.Rows.Count:N0} rows returned.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution Error:\n\n{ex.Message}", "Query Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Query failed during execution.";
            }
            finally
            {
                btnRunQuery.Enabled = true;
            }
        }

        /// <summary>
        /// Executes the given SQL against DuckDB, exposing the loaded data as the
        /// "Bars" and "Trades" tables (backed by the Parquet files exported by TradeDataService).
        /// </summary>
        private DataTable RunDuckDbQuery(string sql)
        {
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();

            using (var setupCmd = connection.CreateCommand())
            {
                setupCmd.CommandText =
                    $"CREATE VIEW Bars AS SELECT * FROM read_parquet('{_dataService.BarsParquetPath.Replace("'", "''")}');" +
                    $"CREATE VIEW Trades AS SELECT * FROM read_parquet('{_dataService.TradesParquetPath.Replace("'", "''")}');";
                setupCmd.ExecuteNonQuery();
            }

            using var queryCmd = connection.CreateCommand();
            queryCmd.CommandText = sql;

            using var reader = queryCmd.ExecuteReader();

            var table = new DataTable();
            table.Load(reader);
            return table;
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
