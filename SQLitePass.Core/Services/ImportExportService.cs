using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services
{
    public static class ImportExportService
    {
        public static string ExportToKeePassCsv(IEnumerable<PwEntry> entries, IDictionary<Guid, string> groupNames)
        {
            var sb = new StringBuilder();
            // KeePass CSV standard header: "Group","Title","Username","Password","URL","Notes"
            sb.AppendLine("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\"");

            foreach (var entry in entries)
            {
                string grp = groupNames.TryGetValue(entry.GroupId, out var gName) ? EscapeCsv(gName) : "General";
                sb.AppendLine($"\"{grp}\",\"{EscapeCsv(entry.Title)}\",\"{EscapeCsv(entry.UserName)}\",\"{EscapeCsv(entry.Password)}\",\"{EscapeCsv(entry.Url)}\",\"{EscapeCsv(entry.Notes)}\"");
            }

            return sb.ToString();
        }

        public static List<PwEntry> ImportFromKeePassCsv(string csvContent, Guid defaultGroupId)
        {
            var result = new List<PwEntry>();
            using var reader = new StringReader(csvContent);
            string? line = reader.ReadLine(); // header
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line);
                if (cols.Count >= 5)
                {
                    var entry = new PwEntry
                    {
                        GroupId = defaultGroupId,
                        Title = cols.Count > 1 ? cols[1] : "",
                        UserName = cols.Count > 2 ? cols[2] : "",
                        Password = cols.Count > 3 ? cols[3] : "",
                        Url = cols.Count > 4 ? cols[4] : "",
                        Notes = cols.Count > 5 ? cols[5] : "",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    result.Add(entry);
                }
            }
            return result;
        }

        public static string ExportToXml(IEnumerable<PwGroup> groups, IEnumerable<PwEntry> entries)
        {
            var doc = new XDocument(
                new XElement("KeePassFile",
                    new XElement("Root",
                        new XElement("Group",
                            new XElement("Name", "Root"),
                            new XElement("Entries",
                                BuildXmlEntries(entries)
                            )
                        )
                    )
                )
            );
            return doc.ToString();
        }

        private static List<XElement> BuildXmlEntries(IEnumerable<PwEntry> entries)
        {
            var list = new List<XElement>();
            foreach (var entry in entries)
            {
                list.Add(new XElement("Entry",
                    new XElement("UUID", entry.Id.ToString()),
                    new XElement("String", new XElement("Key", "Title"), new XElement("Value", entry.Title)),
                    new XElement("String", new XElement("Key", "UserName"), new XElement("Value", entry.UserName)),
                    new XElement("String", new XElement("Key", "Password"), new XElement("Value", entry.Password)),
                    new XElement("String", new XElement("Key", "URL"), new XElement("Value", entry.Url)),
                    new XElement("String", new XElement("Key", "Notes"), new XElement("Value", entry.Notes))
                ));
            }
            return list;
        }

        private static string EscapeCsv(string? val)
        {
            if (val == null) return string.Empty;
            return val.Replace("\"", "\"\"");
        }

        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        sb.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            list.Add(sb.ToString());
            return list;
        }
    }
}
