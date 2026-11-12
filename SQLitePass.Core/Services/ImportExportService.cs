using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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

        /// <summary>
        /// Imports items from a Bitwarden unencrypted JSON export file.
        /// </summary>
        public static List<PwEntry> ImportFromBitwardenJson(string jsonContent, Guid defaultGroupId)
        {
            var result = new List<PwEntry>();
            if (string.IsNullOrWhiteSpace(jsonContent)) return result;

            using var doc = JsonDocument.Parse(jsonContent);
            JsonElement itemsElement;

            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("items", out var itemsProp))
            {
                itemsElement = itemsProp;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                itemsElement = doc.RootElement;
            }
            else
            {
                return result;
            }

            foreach (var item in itemsElement.EnumerateArray())
            {
                string title = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string notes = item.TryGetProperty("notes", out var notesProp) ? notesProp.GetString() ?? "" : "";

                string username = "";
                string password = "";
                string url = "";

                if (item.TryGetProperty("login", out var loginProp) && loginProp.ValueKind == JsonValueKind.Object)
                {
                    if (loginProp.TryGetProperty("username", out var uProp)) username = uProp.GetString() ?? "";
                    if (loginProp.TryGetProperty("password", out var pProp)) password = pProp.GetString() ?? "";

                    if (loginProp.TryGetProperty("uris", out var urisProp) && urisProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var uriItem in urisProp.EnumerateArray())
                        {
                            if (uriItem.TryGetProperty("uri", out var singleUri))
                            {
                                string? uStr = singleUri.GetString();
                                if (!string.IsNullOrEmpty(uStr))
                                {
                                    url = uStr;
                                    break;
                                }
                            }
                        }
                    }

                    if (loginProp.TryGetProperty("totp", out var totpProp))
                    {
                        string? totp = totpProp.GetString();
                        if (!string.IsNullOrEmpty(totp))
                        {
                            notes = string.IsNullOrEmpty(notes) ? $"TOTP: {totp}" : $"{notes}\n\nTOTP: {totp}";
                        }
                    }
                }

                var entry = new PwEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = defaultGroupId,
                    Title = string.IsNullOrWhiteSpace(title) ? (string.IsNullOrWhiteSpace(url) ? "Bitwarden Item" : url) : title,
                    UserName = username,
                    Password = password,
                    Url = url,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Custom fields
                if (item.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fieldsProp.EnumerateArray())
                    {
                        string fName = f.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "";
                        string fVal = f.TryGetProperty("value", out var fv) ? fv.GetString() ?? "" : "";
                        bool isHidden = f.TryGetProperty("type", out var ft) && ft.GetInt32() == 1;

                        if (!string.IsNullOrEmpty(fName))
                        {
                            entry.CustomFields.Add(new PwCustomField
                            {
                                EntryId = entry.Id,
                                Key = fName,
                                Value = fVal,
                                IsProtected = isHidden
                            });
                        }
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Imports credentials exported from common web browsers (Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, Opera).
        /// Automatically detects column headers regardless of language and column order.
        /// </summary>
        public static List<PwEntry> ImportFromBrowserCsv(string csvContent, Guid defaultGroupId)
        {
            var result = new List<PwEntry>();
            using var reader = new StringReader(csvContent);
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine)) return result;

            var headers = ParseCsvLine(headerLine);
            int titleIdx = -1;
            int urlIdx = -1;
            int userIdx = -1;
            int passIdx = -1;
            int notesIdx = -1;

            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (h == "name" || h == "title" || h == "titolo" || h == "nome") titleIdx = i;
                else if (h == "url" || h == "website" || h == "sito" || h == "uri") urlIdx = i;
                else if (h == "username" || h == "user name" || h == "login" || h == "utente" || h == "email" || h == "user") userIdx = i;
                else if (h == "password" || h == "pwd" || h == "pass") passIdx = i;
                else if (h == "note" || h == "notes" || h == "comment") notesIdx = i;
            }

            // Fallback for Firefox ("url","username","password",...)
            if (urlIdx == -1 && headers.Count >= 3) urlIdx = 0;
            if (userIdx == -1 && headers.Count >= 3) userIdx = 1;
            if (passIdx == -1 && headers.Count >= 3) passIdx = 2;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line);

                string url = (urlIdx >= 0 && urlIdx < cols.Count) ? cols[urlIdx] : "";
                string user = (userIdx >= 0 && userIdx < cols.Count) ? cols[userIdx] : "";
                string pass = (passIdx >= 0 && passIdx < cols.Count) ? cols[passIdx] : "";
                string notes = (notesIdx >= 0 && notesIdx < cols.Count) ? cols[notesIdx] : "";
                string title = (titleIdx >= 0 && titleIdx < cols.Count) ? cols[titleIdx] : "";

                if (string.IsNullOrWhiteSpace(title))
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        try
                        {
                            var uri = new Uri(url.StartsWith("http") ? url : "https://" + url);
                            title = uri.Host.Replace("www.", "");
                        }
                        catch
                        {
                            title = url;
                        }
                    }
                    else
                    {
                        title = user;
                    }
                }

                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pass))
                    continue;

                result.Add(new PwEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = defaultGroupId,
                    Title = string.IsNullOrWhiteSpace(title) ? "Browser Item" : title,
                    UserName = user,
                    Password = pass,
                    Url = url,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
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
