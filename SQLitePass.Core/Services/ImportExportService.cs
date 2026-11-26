using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services
{
    public static partial class ImportExportService
    {
        public static string ExportToKeePassCsv(IEnumerable<PwEntry> entries, IDictionary<Guid, string> groupNames)
        {
            var sb = new StringBuilder();
            // SQLitePass grouped CSV variant; KeePass 1.x uses a different five-column format.
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
            List<List<string>> records;
            try
            {
                records = ParseCsvRecords(csvContent);
                if (records.Count > 0 && IsKeePass1CsvHeader(records[0]))
                    records = ParseCsvRecords(csvContent, keepassBackslashEscapes: true);
            }
            catch (InvalidDataException)
            {
                // KeePass 1.x escapes quotes with backslashes instead of RFC 4180 doubled quotes.
                records = ParseCsvRecords(csvContent, keepassBackslashEscapes: true);
            }
            if (records.Count <= 1) return result;

            string[] headers = records[0].Select(NormalizeCsvHeader).ToArray();
            if (headers.Any(string.IsNullOrEmpty))
                throw new InvalidDataException("CSV: ogni colonna deve avere un'intestazione.");
            if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                throw new InvalidDataException("CSV: le intestazioni duplicate rendono ambigua l'importazione.");

            int FindColumn(params string[] aliases)
            {
                var accepted = aliases.Select(NormalizeCsvHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int found = -1;
                for (int i = 0; i < headers.Length; i++)
                {
                    if (!accepted.Contains(headers[i])) continue;
                    if (found >= 0) throw new InvalidDataException("CSV ambiguo: più colonne corrispondono allo stesso campo.");
                    found = i;
                }
                return found;
            }

            int groupIndex = FindColumn("Group", "Folder", "Group Path", "Folder Path");
            int titleIndex = FindColumn("Title", "Account", "Name");
            int userIndex = FindColumn("Username", "User Name", "Login Name", "Login", "User", "Email");
            int passwordIndex = FindColumn("Password", "Pwd", "Pass");
            int urlIndex = FindColumn("URL", "Web Site", "Website", "URI", "Site");
            int notesIndex = FindColumn("Notes", "Comments", "Comment");
            if (titleIndex < 0 || userIndex < 0 || passwordIndex < 0)
                throw new InvalidDataException("Formato CSV non riconosciuto. Servono colonne Account/Title, Login Name/Username e Password.");

            var mappedColumns = new HashSet<int> { titleIndex, userIndex, passwordIndex };
            foreach (int index in new[] { groupIndex, urlIndex, notesIndex })
                if (index >= 0) mappedColumns.Add(index);

            for (int r = 1; r < records.Count; r++)
            {
                var cols = records[r];
                if (cols.Count != records[0].Count) throw new InvalidDataException($"CSV: numero di colonne non valido al record {r + 1}.");
                var entry = new PwEntry
                {
                    GroupId = defaultGroupId,
                    SourceGroupPath = groupIndex >= 0 ? cols[groupIndex] : "",
                    Title = cols[titleIndex],
                    UserName = cols[userIndex],
                    Password = cols[passwordIndex],
                    Url = urlIndex >= 0 ? cols[urlIndex] : "",
                    Notes = notesIndex >= 0 ? cols[notesIndex] : "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                for (int i = 0; i < headers.Length; i++)
                {
                    if (mappedColumns.Contains(i)) continue;
                    entry.CustomFields.Add(new PwCustomField
                    {
                        EntryId = entry.Id,
                        Key = records[0][i].Trim(),
                        Value = cols[i],
                        IsProtected = true
                    });
                }
                result.Add(entry);
            }
            return result;
        }

        private static string NormalizeCsvHeader(string header) =>
            string.Join(" ", header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        private static bool IsKeePass1CsvHeader(IReadOnlyList<string> headers)
        {
            var normalized = headers.Select(NormalizeCsvHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return normalized.Contains("account") && normalized.Contains("login name");
        }

        /// <summary>
        /// Imports items from a Bitwarden unencrypted JSON export file.
        /// </summary>
        public static List<PwEntry> ImportFromBitwardenJson(string jsonContent, Guid defaultGroupId)
        {
            var result = new List<PwEntry>();
            if (string.IsNullOrWhiteSpace(jsonContent)) return result;
            if (jsonContent.Length > 32 * 1024 * 1024) throw new InvalidDataException("Il JSON supera 32 MB.");

            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True)
                throw new InvalidDataException("Usare un'esportazione Bitwarden JSON non cifrata.");
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
                throw new InvalidDataException("Esportazione Bitwarden non riconosciuta o cifrata.");
            }

            if (itemsElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("items must be an array.");
            if (itemsElement.GetArrayLength() > 100000) throw new InvalidDataException("Too many imported entries.");
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Imported items must be objects.");
                if (item.TryGetProperty("type", out var declaredType) && (declaredType.ValueKind != JsonValueKind.Number || !declaredType.TryGetInt32(out _)))
                    throw new InvalidDataException("Imported item type must be an integer.");
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
                    SourceItemType = item.TryGetProperty("type", out var sourceType) && sourceType.TryGetInt32(out int type) ? type : item.TryGetProperty("login", out _) ? 1 : 0,
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

                // Preserve fields that do not have a native equivalent (cards, identities, passkeys, additional URIs).
                entry.CustomFields.Add(new PwCustomField { EntryId = entry.Id, Key = "BitwardenOriginalJson", Value = item.GetRawText(), IsProtected = true });
                if (item.TryGetProperty("folderId", out var folderId) && folderId.ValueKind == JsonValueKind.String &&
                    doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Array)
                {
                    var matches = folders.EnumerateArray().Where(folder => folder.TryGetProperty("id", out var id) && id.GetString() == folderId.GetString()).ToArray();
                    if (matches.Length == 1 && matches[0].TryGetProperty("name", out var folderName)) entry.SourceGroupPath = folderName.GetString();
                }

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
            var records = ParseCsvRecords(csvContent);
            if (records.Count <= 1) return result;

            var headers = records[0];
            int titleIdx = -1;
            int urlIdx = -1;
            int userIdx = -1;
            int passIdx = -1;
            int notesIdx = -1;
            static void SetColumn(ref int destination, int index)
            {
                if (destination != -1) throw new InvalidDataException("CSV ambiguo: più colonne corrispondono allo stesso campo.");
                destination = index;
            }

            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (h == "name" || h == "title" || h == "titolo" || h == "nome") SetColumn(ref titleIdx, i);
                else if (h == "url" || h == "website" || h == "sito" || h == "uri") SetColumn(ref urlIdx, i);
                else if (h == "username" || h == "user name" || h == "login" || h == "utente" || h == "email" || h == "user") SetColumn(ref userIdx, i);
                else if (h == "password" || h == "pwd" || h == "pass") SetColumn(ref passIdx, i);
                else if (h == "note" || h == "notes" || h == "comment") SetColumn(ref notesIdx, i);
            }

            if (urlIdx == -1 || userIdx == -1 || passIdx == -1)
                throw new InvalidDataException("Intestazioni CSV non riconosciute. Sono richieste colonne URL, username e password.");

            for (int r = 1; r < records.Count; r++)
            {
                var cols = records[r];
                if (cols.Count != headers.Count) throw new InvalidDataException($"CSV: numero di colonne non valido al record {r + 1}.");
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

        private static string EscapeCsv(string? val)
        {
            if (val == null) return string.Empty;
            return val.Replace("\"", "\"\"");
        }

        /// <summary>
        /// Delimited-text parser supporting RFC 4180 quoting, embedded line breaks, and optional KeePass escapes.
        /// </summary>
        public static List<List<string>> ParseCsvRecords(string csvContent, bool keepassBackslashEscapes = false) =>
            CsvParser.Parse(csvContent, keepassBackslashEscapes);
    }
}
