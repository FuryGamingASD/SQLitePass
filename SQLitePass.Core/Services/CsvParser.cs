using System.Text;

namespace SQLitePass.Core.Services;

internal static class CsvParser
{
    internal static List<List<string>> Parse(string input)
    {
        if (input.Length > 32 * 1024 * 1024) throw new InvalidDataException("Il CSV supera 32 MB.");
        input = input.TrimStart('\uFEFF');
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false; int line = 1;
        void EndField()
        {
            row.Add(field.ToString()); field.Clear(); closed = false;
            if (row.Count > 256) throw new InvalidDataException($"CSV: troppe colonne alla riga {line}.");
        }
        void EndRow()
        {
            EndField(); if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0])) rows.Add(row);
            row = [];
            if (rows.Count > 100001) throw new InvalidDataException("Il CSV supera 100.000 record.");
        }
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(ch); if (ch == '\n') line++; }
            }
            else if (ch == ',') EndField();
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
                EndRow(); line++;
            }
            else if (ch == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || ch == '"') throw new InvalidDataException($"CSV: delimitazione non valida alla riga {line}.");
            else field.Append(ch);
            if (field.Length > 1024 * 1024) throw new InvalidDataException($"CSV: campo troppo grande alla riga {line}.");
        }
        if (quoted) throw new InvalidDataException($"CSV: virgolette non chiuse alla riga {line}.");
        if (field.Length > 0 || row.Count > 0 || closed) EndRow();
        return rows;
    }
}
