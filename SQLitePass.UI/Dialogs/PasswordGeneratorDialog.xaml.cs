using System;
using System.Windows;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class PasswordGeneratorDialog : Window
    {
        private bool _isInitialized = false;
        public string ResultPassword { get; private set; } = string.Empty;

        public PasswordGeneratorDialog()
        {
            InitializeComponent();
            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);
            _isInitialized = true;
            GenerateNewPassword();
            Closed += (_, _) => { TxtGeneratedPassword.Clear(); if (DialogResult != true) ResultPassword = string.Empty; };
        }

        private void GenerateNewPassword()
        {
            if (!_isInitialized || TxtGeneratedPassword == null || SliderLength == null ||
                ChkUpper == null || ChkLower == null || ChkDigits == null ||
                ChkSpecial == null || ChkBrackets == null || ChkExcludeLookAlike == null ||
                PbEntropy == null || TxtEntropy == null)
            {
                return;
            }

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

            try { ResultPassword = PasswordGenerator.Generate(options); }
            catch (ArgumentException)
            {
                ResultPassword = string.Empty; TxtGeneratedPassword.Text = SQLitePass.UI.Services.LocalizationManager.GetString("GeneratorChooseClass");
                PbEntropy.Value = 0; TxtEntropy.Text = "0/100"; return;
            }
            TxtGeneratedPassword.Text = ResultPassword;

            double entropy = PasswordGenerator.CalculateEntropy(ResultPassword);
            PbEntropy.Value = entropy;
            TxtEntropy.Text = SQLitePass.UI.Services.LocalizationManager.GetString("QualityScore", entropy);
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
                SQLitePass.UI.Services.ClipboardCoordinator.Shared.CopyWithAutoClear(ResultPassword, SQLitePass.UI.Services.LocalizationManager.GetString("GeneratedPassword"));
            }
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ResultPassword)) return;
            SQLitePass.UI.Services.ClipboardCoordinator.Shared.CopyWithAutoClear(ResultPassword, SQLitePass.UI.Services.LocalizationManager.GetString("GeneratedPassword"));
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
