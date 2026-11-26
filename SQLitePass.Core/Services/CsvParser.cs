using System.Text;

namespace SQLitePass.Core.Services;

internal static class CsvParser
{
    internal static List<List<string>> Parse(string input, bool keepassBackslashEscapes = false)
    {
        if (input.Length > 32 * 1024 * 1024) throw new InvalidDataException("Il CSV supera 32 MB.");
        input = input.TrimStart('\uFEFF');
        char delimiter = DetectDelimiter(input, keepassBackslashEscapes);
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
                if (keepassBackslashEscapes && ch == '\\' && i + 1 < input.Length &&
                    (input[i + 1] is '\\' or '"' || input[i + 1] == delimiter))
                {
                    field.Append(input[++i]);
                }
                else if (ch == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(ch); if (ch == '\n') line++; }
            }
            else if (ch == delimiter) EndField();
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

    private static char DetectDelimiter(string input, bool keepassBackslashEscapes)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        bool quoted = false;
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            if (quoted && keepassBackslashEscapes && ch == '\\' && i + 1 < input.Length &&
                (input[i + 1] is '\\' or '"' || input[i + 1] == ','))
            {
                i++;
                continue;
            }
            if (ch == '"')
            {
                if (quoted && i + 1 < input.Length && input[i + 1] == '"') { i++; continue; }
                quoted = !quoted;
                continue;
            }
            if (quoted) continue;
            if (ch is '\r' or '\n') break;
            if (counts.ContainsKey(ch)) counts[ch]++;
        }

        char delimiter = ',';
        foreach (char candidate in new[] { ';', '\t' })
            if (counts[candidate] > counts[delimiter]) delimiter = candidate;
        return delimiter;
    }
}
