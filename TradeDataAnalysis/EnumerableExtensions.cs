using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace TradeDataAnalysis
{
    public static class EnumerableExtensions
    {
        public static DataTable ToDataTable(this IEnumerable items)
        {
            var table = new DataTable();
            PropertyInfo[] props = null;

            foreach (var item in items)
            {
                if (item == null) continue;

                if (props == null)
                {
                    props = item.GetType().GetProperties();
                    foreach (var prop in props)
                    {
                        Type propType = prop.PropertyType;
                        // Handle Nullable<T> types
                        if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            propType = Nullable.GetUnderlyingType(propType);
                        }
                        table.Columns.Add(prop.Name, propType ?? typeof(object));
                    }
                }

                var row = table.NewRow();
                foreach (var prop in props)
                {
                    row[prop.Name] = prop.GetValue(item) ?? DBNull.Value;
                }
                table.Rows.Add(row);
            }
            return table;
        }
    }
}