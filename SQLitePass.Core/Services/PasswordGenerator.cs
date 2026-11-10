using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SQLitePass.Core.Services
{
    public class PasswordGeneratorOptions
    {
        public int Length { get; set; } = 20;
        public bool IncludeUpperCase { get; set; } = true;
        public bool IncludeLowerCase { get; set; } = true;
        public bool IncludeDigits { get; set; } = true;
        public bool IncludeSpecial { get; set; } = true;
        public bool IncludeBrackets { get; set; } = false;
        public bool ExcludeLookAlike { get; set; } = true;
        public string CustomCharacters { get; set; } = string.Empty;
    }

    public static class PasswordGenerator
    {
        public const string UpperCaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public const string LowerCaseChars = "abcdefghijklmnopqrstuvwxyz";
        public const string DigitChars = "0123456789";
        public const string SpecialChars = "!@#$%^&*_-+=~|:;,.?";
        public const string BracketChars = "()[]{}<>";
        public const string LookAlikeChars = "Il1O0|";

        public static string Generate(PasswordGeneratorOptions options)
        {
            if (options.Length < 4)
                options.Length = 4;

            var charSets = new List<string>();

            if (options.IncludeUpperCase) charSets.Add(FilterLookAlike(UpperCaseChars, options.ExcludeLookAlike));
            if (options.IncludeLowerCase) charSets.Add(FilterLookAlike(LowerCaseChars, options.ExcludeLookAlike));
            if (options.IncludeDigits) charSets.Add(FilterLookAlike(DigitChars, options.ExcludeLookAlike));
            if (options.IncludeSpecial) charSets.Add(FilterLookAlike(SpecialChars, options.ExcludeLookAlike));
            if (options.IncludeBrackets) charSets.Add(FilterLookAlike(BracketChars, options.ExcludeLookAlike));

            if (!string.IsNullOrEmpty(options.CustomCharacters))
            {
                charSets.Add(FilterLookAlike(options.CustomCharacters, options.ExcludeLookAlike));
            }

            if (charSets.Count == 0)
            {
                charSets.Add(LowerCaseChars + DigitChars);
            }

            var fullPool = new StringBuilder();
            foreach (var set in charSets)
            {
                fullPool.Append(set);
            }

            string pool = fullPool.ToString();
            if (pool.Length == 0) pool = "abcdefghijklmnopqrstuvwxyz0123456789";

            var resultChars = new List<char>(options.Length);

            // Ensure at least one character from each active charset
            foreach (var set in charSets)
            {
                if (set.Length > 0 && resultChars.Count < options.Length)
                {
                    int index = RandomNumberGenerator.GetInt32(set.Length);
                    resultChars.Add(set[index]);
                }
            }

            // Fill remaining characters from the entire pool
            while (resultChars.Count < options.Length)
            {
                int index = RandomNumberGenerator.GetInt32(pool.Length);
                resultChars.Add(pool[index]);
            }

            // Cryptographic Fisher-Yates shuffle
            for (int i = resultChars.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (resultChars[i], resultChars[j]) = (resultChars[j], resultChars[i]);
            }

            return new string(resultChars.ToArray());
        }

        public static double CalculateEntropy(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0.0;

            int poolSize = 0;
            bool hasUpper = false, hasLower = false, hasDigits = false, hasSpecial = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigits = true;
                else hasSpecial = true;
            }

            if (hasUpper) poolSize += 26;
            if (hasLower) poolSize += 26;
            if (hasDigits) poolSize += 10;
            if (hasSpecial) poolSize += 32;

            if (poolSize == 0) return 0.0;
            return Math.Round(Math.Log2(poolSize) * password.Length, 1);
        }

        private static string FilterLookAlike(string input, bool exclude)
        {
            if (!exclude) return input;
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (!LookAlikeChars.Contains(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
