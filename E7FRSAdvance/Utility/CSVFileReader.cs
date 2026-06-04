using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace E7FRSAdvance.Utility
{
    public class CSVFileReader
    {
        public DataTable ReadFile(string filePath)
        {
            DataTable dataTable = new DataTable();
            if (File.Exists(filePath))
            {
                using (StreamReader streamReader = new StreamReader(filePath))
                {
                    dataTable = ReadWithSpecialChar(streamReader.ReadToEnd(), ",");
                }
            }
            return dataTable;
        }

        public DataTable ReadWithSpecialChar(string csvString, params string[] args)
        {
            DataTable dataTable = new DataTable();
            using (TextFieldParser textFieldParser = new TextFieldParser(new MemoryStream(Encoding.UTF8.GetBytes(csvString))))
            {
                textFieldParser.HasFieldsEnclosedInQuotes = true;
                textFieldParser.TrimWhiteSpace = true;
                textFieldParser.SetDelimiters(args);
                bool readFirstLine = true;
                try
                {
                    while (!textFieldParser.EndOfData)
                    {
                        string[] fields = textFieldParser.ReadFields();

                        if (readFirstLine)
                        {
                            foreach (var val in fields)
                            {
                                dataTable.Columns.Add(val);
                            }
                            readFirstLine = false;
                        }
                        else
                        {
                            dataTable.Rows.Add(fields);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return dataTable;
        }

        public DataTable RemoveEscapeSequences(DataTable dataTable)
        {
            if (dataTable != null && dataTable.Columns != null && dataTable.Columns.Count > 0)
            {
                foreach (DataColumn column in dataTable.Columns)
                {
                    column.ColumnName = Regex.Unescape(column.ColumnName);
                }
            }
            return dataTable;
        }
    }
}