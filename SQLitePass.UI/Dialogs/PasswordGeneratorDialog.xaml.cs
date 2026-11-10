using System;
using System.Windows;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class PasswordGeneratorDialog : Window
    {
        public string ResultPassword { get; private set; } = string.Empty;

        public PasswordGeneratorDialog()
        {
            InitializeComponent();
            GenerateNewPassword();
        }

        private void GenerateNewPassword()
        {
            if (TxtGeneratedPassword == null) return;

            var options = new PasswordGeneratorOptions
            {
                Length = (int)SliderLength.Value,
                IncludeUpperCase = ChkUpper.IsChecked == true,
                IncludeLowerCase = ChkLower.IsChecked == true,
                IncludeDigits = ChkDigits.IsChecked == true,
                IncludeSpecial = ChkSpecial.IsChecked == true,
                IncludeBrackets = ChkBrackets.IsChecked == true,
                ExcludeLookAlike = ChkExcludeLookAlike.IsChecked == true
            };

            ResultPassword = PasswordGenerator.Generate(options);
            TxtGeneratedPassword.Text = ResultPassword;

            double entropy = PasswordGenerator.CalculateEntropy(ResultPassword);
            PbEntropy.Value = Math.Min(entropy, 128);
            TxtEntropy.Text = $"{entropy:F1} bits";
        }

        private void SliderLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            GenerateNewPassword();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            GenerateNewPassword();
        }

        private void BtnRegenerate_Click(object sender, RoutedEventArgs e)
        {
            GenerateNewPassword();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResultPassword))
            {
                Clipboard.SetText(ResultPassword);
                MessageBox.Show("Password copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
