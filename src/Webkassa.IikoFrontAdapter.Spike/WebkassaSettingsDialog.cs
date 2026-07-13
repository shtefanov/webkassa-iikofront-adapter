using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resto.Front.Api;

namespace Webkassa.IikoFrontAdapter.Spike;

public static class WebkassaSettingsDialog
{
    public static void Show()
    {
        var ownerHandle = NativeMethods.GetForegroundWindow();
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using (var form = new SettingsForm(ownerHandle))
                {
                    if (ownerHandle == IntPtr.Zero)
                        form.ShowDialog();
                    else
                        form.ShowDialog(new WindowHandle(ownerHandle));
                }
            }
            catch (Exception error)
            {
                ShowTopMostMessage(error.Message, "Ошибка настроек Webkassa", MessageBoxIcon.Error);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
    }

    private sealed class SettingsForm : Form
    {
        private const string DeveloperCaption = "Разработано shtefanov";
        private const string DeveloperSiteCaption = "iiko-plugin.kz";
        private const string DeveloperSiteUrl = "https://iiko-plugin.kz";

        private readonly ComboBox authMode = new ComboBox();
        private readonly TextBox environment = new TextBox();
        private readonly TextBox baseUrl = new TextBox();
        private readonly TextBox companyProfile = new TextBox();
        private readonly TextBox cashboxUniqueNumber = new TextBox();
        private readonly TextBox apiKey = new TextBox();
        private readonly TextBox login = new TextBox();
        private readonly TextBox password = new TextBox();
        private readonly Button passwordReveal = new Button();
        private readonly ToolTip tooltips = new ToolTip();
        private readonly ComboBox printingMode = new ComboBox();
        private readonly ComboBox printerName = new ComboBox();
        private readonly TextBox pdfOutputDirectory = new TextBox();
        private readonly ComboBox paperKind = new ComboBox();
        private readonly TextBox acceptLanguage = new TextBox();
        private readonly NumericUpDown loggingRetentionDays = new NumericUpDown();
        private readonly Label connectionStatus = new Label();
        private readonly TextBox nationalCatalogBaseUrl = new TextBox();
        private readonly TextBox nationalCatalogApiKey = new TextBox();
        private readonly TextBox nationalCatalogLogin = new TextBox();
        private readonly TextBox nationalCatalogPassword = new TextBox();
        private readonly CheckBox nationalCatalogEnabled = new CheckBox();
        private readonly CheckBox nationalCatalogDryRun = new CheckBox();
        private readonly NumericUpDown nationalCatalogBatchSize = new NumericUpDown();
        private readonly NumericUpDown nationalCatalogAutoBatchLimit = new NumericUpDown();
        private readonly NumericUpDown nationalCatalogAutoDelaySeconds = new NumericUpDown();
        private readonly TextBox nationalCatalogProducerName = new TextBox();
        private readonly TextBox nationalCatalogProducerTin = new TextBox();
        private readonly TextBox nationalCatalogBrand = new TextBox();
        private readonly TextBox nationalCatalogCountryName = new TextBox();
        private readonly TextBox nationalCatalogDefaultOktru = new TextBox();
        private readonly TextBox nationalCatalogDefaultMeasureName = new TextBox();
        private readonly CheckBox nationalCatalogTreatDishAsOwnProduction = new CheckBox();
        private readonly CheckBox nationalCatalogTreatGoodsWithoutBarcodeAsOwnProduction = new CheckBox();
        private readonly CheckBox nationalCatalogAutoPublication = new CheckBox();
        private readonly Label nationalCatalogStatus = new Label();
        private readonly IntPtr ownerHandle;

        private AdapterConfiguration configuration = new AdapterConfiguration();
        private bool passwordVisible;

        public SettingsForm(IntPtr ownerHandle)
        {
            this.ownerHandle = ownerHandle;
            Text = "Настройки Webkassa";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = ownerHandle == IntPtr.Zero;
            MinimumSize = new Size(680, 740);
            Size = new Size(780, 820);
            Font = new Font("Segoe UI", 9F);

            authMode.DropDownStyle = ComboBoxStyle.DropDownList;
            authMode.Items.Add(new ComboItem("API key + логин/пароль", AdapterAuthOptions.ApiKeyAndLoginPasswordMode));
            authMode.Items.Add(new ComboItem("Только логин/пароль", AdapterAuthOptions.LoginPasswordOnlyMode));
            authMode.SelectedIndexChanged += (_, _) => UpdateApiKeyState();

            printingMode.DropDownStyle = ComboBoxStyle.DropDownList;
            printingMode.Items.Add(new ComboItem("Принтер iiko, затем Windows/PDF", AdapterPrintingOptions.IikoReceiptPrinterWithWindowsFallbackMode));
            printingMode.Items.Add(new ComboItem("Только принтер iiko", AdapterPrintingOptions.IikoReceiptPrinterOnlyMode));
            printingMode.Items.Add(new ComboItem("Windows-принтер", AdapterPrintingOptions.WindowsPrinterMode));
            printingMode.Items.Add(new ComboItem("PDF через Microsoft Print to PDF", AdapterPrintingOptions.WindowsPdfMode));

            paperKind.DropDownStyle = ComboBoxStyle.DropDownList;
            paperKind.Items.Add(new ComboItem("80 мм", "0"));
            paperKind.Items.Add(new ComboItem("57/58 мм", "3"));
            paperKind.Items.Add(new ComboItem("A4 portrait", "12"));
            paperKind.Items.Add(new ComboItem("A4 landscape", "13"));

            loggingRetentionDays.Minimum = 1;
            loggingRetentionDays.Maximum = 3650;
            loggingRetentionDays.Value = 30;

            printerName.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string installedPrinter in PrinterSettings.InstalledPrinters)
                printerName.Items.Add(installedPrinter);

            apiKey.UseSystemPasswordChar = true;
            password.UseSystemPasswordChar = true;
            ConfigurePasswordRevealButton();
            nationalCatalogApiKey.UseSystemPasswordChar = true;
            nationalCatalogPassword.UseSystemPasswordChar = true;
            nationalCatalogEnabled.Text = "Включить интеграцию National Catalog";
            nationalCatalogDryRun.Text = "Dry run: не создавать заявки автоматически";
            nationalCatalogTreatDishAsOwnProduction.Text = "Dish считать собственным производством";
            nationalCatalogTreatGoodsWithoutBarcodeAsOwnProduction.Text = "Goods без штрихкода считать собственным производством";
            nationalCatalogAutoPublication.Text = "autoPublication для заявок";
            nationalCatalogBatchSize.Minimum = 1;
            nationalCatalogBatchSize.Maximum = 100;
            nationalCatalogBatchSize.Value = 10;
            nationalCatalogAutoBatchLimit.Minimum = 1;
            nationalCatalogAutoBatchLimit.Maximum = 20;
            nationalCatalogAutoBatchLimit.Value = 3;
            nationalCatalogAutoDelaySeconds.Minimum = 0;
            nationalCatalogAutoDelaySeconds.Maximum = 300;
            nationalCatalogAutoDelaySeconds.Value = 30;

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16),
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var webkassaTab = new TabPage("Webkassa");
            var catalogTab = new TabPage("Каталог НКТ");

            var webkassaLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 15,
                Padding = new Padding(12),
            };
            webkassaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            webkassaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(webkassaLayout, 0, "Режим авторизации", authMode);
            AddRow(webkassaLayout, 1, "Environment", environment);
            AddRow(webkassaLayout, 2, "Base URL", baseUrl);
            AddRow(webkassaLayout, 3, "Company profile", companyProfile);
            AddRow(webkassaLayout, 4, "CashboxUniqueNumber", cashboxUniqueNumber);
            AddRow(webkassaLayout, 5, "API key", apiKey);
            AddRow(webkassaLayout, 6, "Login", login);
            AddRow(webkassaLayout, 7, "Password", BuildPasswordRevealControl(password, passwordReveal));
            AddRow(webkassaLayout, 8, "Режим печати", printingMode);
            AddRow(webkassaLayout, 9, "Windows-принтер", printerName);
            AddRow(webkassaLayout, 10, "PDF folder", pdfOutputDirectory);
            AddRow(webkassaLayout, 11, "Формат чека", paperKind);
            AddRow(webkassaLayout, 12, "Accept-Language", acceptLanguage);
            AddRow(webkassaLayout, 13, "Хранить логи, дней", loggingRetentionDays);
            connectionStatus.Text = "Статус: не проверено";
            connectionStatus.TextAlign = ContentAlignment.MiddleLeft;
            AddRow(webkassaLayout, 14, "Подключение", connectionStatus);

            var catalogLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 23,
                Padding = new Padding(12),
                AutoScroll = true,
            };
            catalogLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            catalogLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(catalogLayout, 0, "", nationalCatalogEnabled);
            AddRow(catalogLayout, 1, "National Catalog URL", nationalCatalogBaseUrl);
            AddRow(catalogLayout, 2, "API key", nationalCatalogApiKey);
            AddRow(catalogLayout, 3, "Login кабинета", nationalCatalogLogin);
            AddRow(catalogLayout, 4, "Password кабинета", nationalCatalogPassword);
            AddRow(catalogLayout, 5, "", nationalCatalogDryRun);
            AddRow(catalogLayout, 6, "Размер пачки", nationalCatalogBatchSize);
            AddRow(catalogLayout, 7, "Авто: максимум пачек", nationalCatalogAutoBatchLimit);
            AddRow(catalogLayout, 8, "Авто: пауза секунд", nationalCatalogAutoDelaySeconds);
            AddRow(catalogLayout, 9, "Производитель", nationalCatalogProducerName);
            AddRow(catalogLayout, 10, "БИН/ИИН производителя", nationalCatalogProducerTin);
            AddRow(catalogLayout, 11, "Бренд", nationalCatalogBrand);
            AddRow(catalogLayout, 12, "Страна производства", nationalCatalogCountryName);
            AddRow(catalogLayout, 13, "OKTRU по умолчанию", nationalCatalogDefaultOktru);
            AddRow(catalogLayout, 14, "Ед. изм. если пусто в iiko", nationalCatalogDefaultMeasureName);
            AddRow(catalogLayout, 15, "", nationalCatalogTreatDishAsOwnProduction);
            AddRow(catalogLayout, 16, "", nationalCatalogTreatGoodsWithoutBarcodeAsOwnProduction);
            AddRow(catalogLayout, 17, "", nationalCatalogAutoPublication);
            nationalCatalogStatus.Text = "Статус: не проверено";
            nationalCatalogStatus.TextAlign = ContentAlignment.MiddleLeft;
            AddRow(catalogLayout, 18, "National Catalog", nationalCatalogStatus);

            var catalogButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
            };
            var exportProducts = new Button { Text = "Экспорт активной номенклатуры", Width = 220, DialogResult = DialogResult.None };
            var buildDrafts = new Button { Text = "Сформировать черновики НКТ", Width = 220, DialogResult = DialogResult.None };
            var refreshDictionaries = new Button { Text = "Обновить справочники", Width = 180, DialogResult = DialogResult.None };
            var prepareBatch = new Button { Text = "Подготовить пачку к отправке", Width = 230, DialogResult = DialogResult.None };
            var submitBatch = new Button { Text = "Отправить следующую пачку", Width = 220, DialogResult = DialogResult.None };
            var runAuto = new Button { Text = "Запустить автообработку", Width = 210, DialogResult = DialogResult.None };
            var refreshStatuses = new Button { Text = "Обновить статусы", Width = 170, DialogResult = DialogResult.None };
            var buildWebNktImport = new Button { Text = "Сформировать импорт WebNKT", Width = 230, DialogResult = DialogResult.None };
            var nktIndexStatus = new Button { Text = "Статус индекса НКТ", Width = 180, DialogResult = DialogResult.None };
            var testNationalCatalog = new Button { Text = "Проверить API", Width = 130, DialogResult = DialogResult.None };
            exportProducts.Click += (_, _) => ExportActiveProducts(exportProducts);
            buildDrafts.Click += (_, _) => BuildNationalCatalogDrafts(buildDrafts);
            refreshDictionaries.Click += (_, _) => RefreshNationalCatalogDictionaries(refreshDictionaries);
            prepareBatch.Click += (_, _) => PrepareNationalCatalogBatch(prepareBatch);
            submitBatch.Click += (_, _) => SubmitNationalCatalogBatch(submitBatch);
            runAuto.Click += (_, _) => RunNationalCatalogAutoProcessing(runAuto);
            refreshStatuses.Click += (_, _) => RefreshNationalCatalogStatuses(refreshStatuses);
            buildWebNktImport.Click += (_, _) => BuildWebNktImport(buildWebNktImport);
            nktIndexStatus.Click += (_, _) => ShowNktIndexStatus(nktIndexStatus);
            testNationalCatalog.Click += (_, _) => TestNationalCatalogConnection(testNationalCatalog);
            catalogButtons.Controls.Add(exportProducts);
            catalogButtons.Controls.Add(buildDrafts);
            catalogButtons.Controls.Add(refreshDictionaries);
            catalogButtons.Controls.Add(prepareBatch);
            catalogButtons.Controls.Add(submitBatch);
            catalogButtons.Controls.Add(runAuto);
            catalogButtons.Controls.Add(refreshStatuses);
            catalogButtons.Controls.Add(buildWebNktImport);
            catalogButtons.Controls.Add(nktIndexStatus);
            catalogButtons.Controls.Add(testNationalCatalog);
            catalogLayout.Controls.Add(catalogButtons, 0, 19);
            catalogLayout.SetColumnSpan(catalogButtons, 2);

            webkassaTab.Controls.Add(webkassaLayout);
            catalogTab.Controls.Add(catalogLayout);
            tabs.TabPages.Add(webkassaTab);
            tabs.TabPages.Add(catalogTab);
            rootLayout.Controls.Add(tabs, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
            };
            var save = new Button { Text = "Сохранить", Width = 120, DialogResult = DialogResult.None };
            var test = new Button { Text = "Тест", Width = 100, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
            save.Click += (_, _) => Save();
            test.Click += (_, _) => TestConnection(test);
            buttons.Controls.Add(save);
            buttons.Controls.Add(test);
            buttons.Controls.Add(cancel);
            rootLayout.Controls.Add(buttons, 0, 1);
            rootLayout.Controls.Add(BuildDeveloperFooter(), 0, 2);

            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(rootLayout);

            Load += (_, _) => LoadConfiguration();
            Shown += (_, _) => BringDialogToFront();
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        private Control BuildPasswordRevealControl(TextBox textBox, Button revealButton)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            textBox.Dock = DockStyle.Fill;
            revealButton.Dock = DockStyle.Fill;
            revealButton.Margin = new Padding(4, 0, 0, 0);
            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(revealButton, 1, 0);
            return panel;
        }

        private void ConfigurePasswordRevealButton()
        {
            passwordReveal.Text = string.Empty;
            passwordReveal.TabStop = false;
            passwordReveal.AccessibleName = "Показать пароль";
            passwordReveal.AccessibleDescription = "Показать или скрыть введенный пароль Webkassa";
            passwordReveal.FlatStyle = FlatStyle.Standard;
            tooltips.SetToolTip(passwordReveal, "Показать пароль");
            passwordReveal.Click += (_, _) => TogglePasswordVisibility();
            passwordReveal.Paint += DrawPasswordRevealIcon;
        }

        private void TogglePasswordVisibility()
        {
            passwordVisible = !passwordVisible;
            password.UseSystemPasswordChar = !passwordVisible;
            var caption = passwordVisible ? "Скрыть пароль" : "Показать пароль";
            passwordReveal.AccessibleName = caption;
            tooltips.SetToolTip(passwordReveal, caption);
            passwordReveal.Invalidate();
            password.Focus();
            password.SelectionStart = password.TextLength;
        }

        private void DrawPasswordRevealIcon(object? sender, PaintEventArgs args)
        {
            if (sender is not Button button)
                return;

            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var color = button.Enabled ? SystemColors.ControlText : SystemColors.GrayText;
            var width = button.ClientSize.Width;
            var height = button.ClientSize.Height;
            var centerY = height / 2;
            var left = Math.Max(6, width / 2 - 10);
            var right = Math.Min(width - 6, width / 2 + 10);
            var centerX = width / 2;

            using (var pen = new Pen(color, 1.6f))
            using (var brush = new SolidBrush(color))
            {
                args.Graphics.DrawBezier(pen, new Point(left, centerY), new Point(left + 5, centerY - 8), new Point(right - 5, centerY - 8), new Point(right, centerY));
                args.Graphics.DrawBezier(pen, new Point(left, centerY), new Point(left + 5, centerY + 8), new Point(right - 5, centerY + 8), new Point(right, centerY));
                args.Graphics.FillEllipse(brush, centerX - 3, centerY - 3, 6, 6);

                if (!passwordVisible)
                    args.Graphics.DrawLine(pen, left + 2, centerY + 9, right - 2, centerY - 9);
            }
        }

        private Control BuildDeveloperFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0),
            };

            var link = new LinkLabel
            {
                AutoSize = true,
                Text = DeveloperSiteCaption,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(8, 1, 0, 0),
            };
            link.Links.Clear();
            link.Links.Add(0, DeveloperSiteCaption.Length, DeveloperSiteUrl);
            link.LinkClicked += (_, args) => OpenDeveloperSite(args.Link.LinkData as string);

            var label = new Label
            {
                AutoSize = true,
                Text = DeveloperCaption,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 2, 0, 0),
            };

            footer.Controls.Add(link);
            footer.Controls.Add(label);
            return footer;
        }

        private static void OpenDeveloperSite(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    $"Не удалось открыть сайт {DeveloperSiteCaption}: {error.Message}",
                    "Webkassa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                configuration = AdapterConfigurationLoader.LoadFromDefaultLocation();
            }
            catch
            {
                configuration = new AdapterConfiguration();
            }

            var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
            environment.Text = configuration.Environment;
            baseUrl.Text = configuration.BaseUrl;
            companyProfile.Text = configuration.CompanyProfile;
            cashboxUniqueNumber.Text = configuration.CashboxUniqueNumber;
            apiKey.Text = string.Empty;
            login.Text = ResolveSecretBestEffort(provider, configuration.SecretRefs?.Login, "login");
            password.Text = string.Empty;
            pdfOutputDirectory.Text = (configuration.Printing ?? new AdapterPrintingOptions()).PdfOutputDirectory;
            acceptLanguage.Text = FirstNonEmpty((configuration.Printing ?? new AdapterPrintingOptions()).AcceptLanguage, "ru-RU");
            var logging = configuration.Logging ?? new AdapterLoggingOptions();
            loggingRetentionDays.Value = Math.Max(loggingRetentionDays.Minimum, Math.Min(loggingRetentionDays.Maximum, logging.RetentionDays <= 0 ? 30 : logging.RetentionDays));
            var nationalCatalog = configuration.NationalCatalog ?? new AdapterNationalCatalogOptions();
            nationalCatalogEnabled.Checked = nationalCatalog.Enabled;
            nationalCatalogBaseUrl.Text = FirstNonEmpty(nationalCatalog.BaseUrl, "https://nationalcatalog.kz/gwp");
            nationalCatalogApiKey.Text = ResolveSecretBestEffort(provider, nationalCatalog.SecretRefs?.ApiKey, "national catalog api key");
            nationalCatalogLogin.Text = ResolveSecretBestEffort(provider, nationalCatalog.SecretRefs?.Login, "national catalog login");
            nationalCatalogPassword.Text = ResolveSecretBestEffort(provider, nationalCatalog.SecretRefs?.Password, "national catalog password");
            nationalCatalogDryRun.Checked = nationalCatalog.DryRun;
            nationalCatalogBatchSize.Value = Math.Max(nationalCatalogBatchSize.Minimum, Math.Min(nationalCatalogBatchSize.Maximum, nationalCatalog.BatchSize <= 0 ? 10 : nationalCatalog.BatchSize));
            nationalCatalogAutoBatchLimit.Value = Math.Max(nationalCatalogAutoBatchLimit.Minimum, Math.Min(nationalCatalogAutoBatchLimit.Maximum, nationalCatalog.AutoBatchLimit <= 0 ? 3 : nationalCatalog.AutoBatchLimit));
            nationalCatalogAutoDelaySeconds.Value = Math.Max(nationalCatalogAutoDelaySeconds.Minimum, Math.Min(nationalCatalogAutoDelaySeconds.Maximum, nationalCatalog.AutoDelaySeconds < 0 ? 30 : nationalCatalog.AutoDelaySeconds));
            var autoFill = nationalCatalog.AutoFill ?? new AdapterNationalCatalogAutoFillOptions();
            nationalCatalogProducerName.Text = autoFill.ProducerName;
            nationalCatalogProducerTin.Text = autoFill.ProducerTin;
            nationalCatalogBrand.Text = autoFill.Brand;
            nationalCatalogCountryName.Text = FirstNonEmpty(autoFill.CountryName, "Казахстан");
            nationalCatalogDefaultOktru.Text = autoFill.DefaultOktru;
            nationalCatalogDefaultMeasureName.Text = FirstNonEmpty(autoFill.DefaultMeasureName, "порция");
            nationalCatalogTreatDishAsOwnProduction.Checked = autoFill.TreatDishAsOwnProduction;
            nationalCatalogTreatGoodsWithoutBarcodeAsOwnProduction.Checked = autoFill.TreatGoodsWithoutBarcodeAsOwnProduction;
            nationalCatalogAutoPublication.Checked = autoFill.AutoPublication;

            SelectCombo(authMode, (configuration.Auth ?? new AdapterAuthOptions()).Mode);
            SelectCombo(printingMode, (configuration.Printing ?? new AdapterPrintingOptions()).Mode);
            SelectCombo(paperKind, (configuration.Printing ?? new AdapterPrintingOptions()).PaperKind.ToString());
            printerName.Text = FirstNonEmpty(
                configuration.Printing?.PreferredWindowsPrinterName,
                configuration.Printing?.FallbackWindowsPrinterName,
                "Microsoft Print to PDF");

            UpdateApiKeyState();
        }

        private void Save()
        {
            try
            {
                var mode = SelectedValue(authMode);
                var cashbox = cashboxUniqueNumber.Text.Trim();
                var env = string.IsNullOrWhiteSpace(environment.Text) ? "prod" : environment.Text.Trim();
                var secretPrefix = $"Webkassa {env} {cashbox}";
                var existingApiKeyRef = configuration.SecretRefs?.ApiKey ?? string.Empty;
                var existingLoginRef = configuration.SecretRefs?.Login ?? string.Empty;
                var existingPasswordRef = configuration.SecretRefs?.Password ?? string.Empty;

                configuration.Environment = env;
                configuration.BaseUrl = baseUrl.Text.Trim();
                configuration.CompanyProfile = companyProfile.Text.Trim();
                configuration.CashboxUniqueNumber = cashbox;
                configuration.Auth = new AdapterAuthOptions { Mode = mode };
                configuration.SecretRefs = new AdapterSecretReferences
                {
                    ApiKey = mode == AdapterAuthOptions.LoginPasswordOnlyMode
                        ? string.Empty
                        : SecretRefForSave(existingApiKeyRef, $"{secretPrefix} api key", apiKey.Text),
                    Login = SecretRefForSave(existingLoginRef, $"{secretPrefix} login", login.Text),
                    Password = SecretRefForSave(existingPasswordRef, $"{secretPrefix} password", password.Text),
                };
                configuration.Printing = new AdapterPrintingOptions
                {
                    Mode = SelectedValue(printingMode),
                    PreferredWindowsPrinterName = printerName.Text.Trim(),
                    FallbackWindowsPrinterName = FirstNonEmpty(printerName.Text.Trim(), "Microsoft Print to PDF"),
                    PdfOutputDirectory = FirstNonEmpty(pdfOutputDirectory.Text.Trim(), @"C:\OpenClaw\logs\webkassa-receipts"),
                    PaperKind = ParsePaperKind(SelectedValue(paperKind)),
                    AcceptLanguage = FirstNonEmpty(acceptLanguage.Text.Trim(), "ru-RU"),
                };
                configuration.Logging = new AdapterLoggingOptions
                {
                    Level = FirstNonEmpty(configuration.Logging?.Level, "info"),
                    RedactSecrets = configuration.Logging?.RedactSecrets ?? true,
                    RetentionDays = (int)loggingRetentionDays.Value,
                };
                var nationalCatalogExisting = configuration.NationalCatalog ?? new AdapterNationalCatalogOptions();
                var nationalCatalogExistingApiKeyRef = nationalCatalogExisting.SecretRefs?.ApiKey ?? string.Empty;
                var nationalCatalogExistingLoginRef = nationalCatalogExisting.SecretRefs?.Login ?? string.Empty;
                var nationalCatalogExistingPasswordRef = nationalCatalogExisting.SecretRefs?.Password ?? string.Empty;
                var nationalCatalogSecretPrefix = $"National Catalog {env}";
                configuration.NationalCatalog = BuildNationalCatalogOptionsFromControls(nationalCatalogExisting, nationalCatalogSecretPrefix);
                configuration.NationalCatalog.SecretRefs = new AdapterSecretReferences
                {
                    ApiKey = SecretRefForSave(nationalCatalogExistingApiKeyRef, $"{nationalCatalogSecretPrefix} api key", nationalCatalogApiKey.Text),
                    Login = SecretRefForSave(nationalCatalogExistingLoginRef, $"{nationalCatalogSecretPrefix} login", nationalCatalogLogin.Text),
                    Password = SecretRefForSave(nationalCatalogExistingPasswordRef, $"{nationalCatalogSecretPrefix} password", nationalCatalogPassword.Text),
                };

                var errors = configuration.Validate();
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

                var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
                if (configuration.Auth.RequiresApiKey())
                {
                    if (string.IsNullOrWhiteSpace(apiKey.Text) && string.IsNullOrWhiteSpace(existingApiKeyRef))
                        throw new InvalidOperationException("API key is required for API key + login/password mode.");
                }
                if (configuration.NationalCatalog.Enabled && string.IsNullOrWhiteSpace(nationalCatalogApiKey.Text) && string.IsNullOrWhiteSpace(nationalCatalogExistingApiKeyRef))
                    throw new InvalidOperationException("National Catalog API key is required when National Catalog integration is enabled.");
                if (string.IsNullOrWhiteSpace(password.Text) && string.IsNullOrWhiteSpace(existingPasswordRef))
                    throw new InvalidOperationException("Password is required.");

                if (configuration.Auth.RequiresApiKey() && !string.IsNullOrWhiteSpace(apiKey.Text))
                    provider.ProtectToFile(configuration.SecretRefs.ApiKey, apiKey.Text.Trim(), "api key");
                if (!string.IsNullOrWhiteSpace(login.Text))
                    provider.ProtectToFile(configuration.SecretRefs.Login, login.Text.Trim(), "login");
                else if (string.IsNullOrWhiteSpace(existingLoginRef))
                    throw new InvalidOperationException("Login is required.");
                if (!string.IsNullOrEmpty(password.Text))
                    provider.ProtectToFile(configuration.SecretRefs.Password, password.Text, "password");
                if (!string.IsNullOrWhiteSpace(nationalCatalogApiKey.Text))
                    provider.ProtectToFile(configuration.NationalCatalog.SecretRefs.ApiKey, nationalCatalogApiKey.Text.Trim(), "national catalog api key");
                if (!string.IsNullOrWhiteSpace(nationalCatalogLogin.Text))
                    provider.ProtectToFile(configuration.NationalCatalog.SecretRefs.Login, nationalCatalogLogin.Text.Trim(), "national catalog login");
                if (!string.IsNullOrEmpty(nationalCatalogPassword.Text))
                    provider.ProtectToFile(configuration.NationalCatalog.SecretRefs.Password, nationalCatalogPassword.Text, "national catalog password");

                var configPath = AdapterConfigurationLoader.GetDefaultConfigPath();
                var configDirectory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(configDirectory))
                    Directory.CreateDirectory(configDirectory);
                File.WriteAllText(configPath, AdapterConfigurationLoader.ToRedactedJson(configuration));

                MessageBox.Show(
                    this,
                    "Настройки Webkassa сохранены. Чтобы sidecar перечитал их, перезапустите службу Webkassa sidecar или iikoFront.",
                    "Webkassa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Ошибка настроек Webkassa", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportActiveProducts(Button exportButton)
        {
            exportButton.Enabled = false;
            try
            {
                var result = IikoProductCatalogExporter.ExportActiveProducts(PluginContext.Operations);
                nationalCatalogStatus.Text = $"Статус: экспортировано {result.ProductCount}, исключено {result.ExcludedByPriceCount}";
                PluginContext.Log.Info($"Webkassa NKT tab exported active iiko product catalog. SourceProductCount={result.SourceProductCount}, ProductCount={result.ProductCount}, ExcludedByPriceCount={result.ExcludedByPriceCount}, JsonPath={result.JsonPath}, CsvPath={result.CsvPath}");
                MessageBox.Show(
                    this,
                    $"Экспортировано позиций с ценой > 0: {result.ProductCount}.{Environment.NewLine}Исключено без цены: {result.ExcludedByPriceCount} из {result.SourceProductCount}.{Environment.NewLine}JSON: {result.JsonPath}{Environment.NewLine}CSV: {result.CsvPath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка экспорта";
                PluginContext.Log.Error($"Webkassa NKT tab active catalog export failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось экспортировать активную номенклатуру iiko: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                exportButton.Enabled = true;
            }
        }

        private void BuildNationalCatalogDrafts(Button buildButton)
        {
            buildButton.Enabled = false;
            try
            {
                var options = BuildNationalCatalogOptionsFromControls(configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(), "National Catalog dry run");
                var result = NationalCatalogDraftExporter.ExportDryRunDrafts(PluginContext.Operations, options);
                nationalCatalogStatus.Text = $"Статус: черновиков {result.DraftReadyCount}, проверить {result.NeedsReviewCount}, пачек {result.BatchCount}";
                PluginContext.Log.Info($"Webkassa NKT tab generated National Catalog dry-run drafts. ProductCount={result.ProductCount}, DraftReady={result.DraftReadyCount}, NeedsReview={result.NeedsReviewCount}, BatchCount={result.BatchCount}, JsonPath={result.JsonPath}, CsvPath={result.CsvPath}");
                MessageBox.Show(
                    this,
                    $"Сформированы локальные черновики НКТ.{Environment.NewLine}Готово к dry-run: {result.DraftReadyCount}.{Environment.NewLine}Требует проверки: {result.NeedsReviewCount}.{Environment.NewLine}Пачек по текущему размеру: {result.BatchCount}.{Environment.NewLine}JSON: {result.JsonPath}{Environment.NewLine}CSV: {result.CsvPath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка черновиков";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog dry-run draft generation failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось сформировать черновики НКТ: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                buildButton.Enabled = true;
            }
        }

        private void RefreshNationalCatalogDictionaries(Button refreshButton)
        {
            refreshButton.Enabled = false;
            nationalCatalogStatus.Text = "Статус: обновление справочников...";
            try
            {
                var request = BuildNationalCatalogTestRequest();
                var result = NationalCatalogDictionaryCache.Refresh(request.BaseUrl, request.ApiKey);
                nationalCatalogStatus.Text = $"Статус: справочники {result.SuccessCount}/{result.EndpointCount}, ошибок {result.FailureCount}";
                PluginContext.Log.Info($"Webkassa NKT tab refreshed National Catalog dictionary cache. Success={result.SuccessCount}, Failure={result.FailureCount}, ManifestPath={result.ManifestPath}");
                MessageBox.Show(
                    this,
                    $"Справочники National Catalog обновлены локально.{Environment.NewLine}Успешно: {result.SuccessCount} из {result.EndpointCount}.{Environment.NewLine}Ошибок: {result.FailureCount}.{Environment.NewLine}Manifest: {result.ManifestPath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка справочников";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog dictionary cache refresh failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось обновить справочники National Catalog: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        }

        private void PrepareNationalCatalogBatch(Button prepareButton)
        {
            prepareButton.Enabled = false;
            try
            {
                var options = BuildNationalCatalogOptionsFromControls(configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(), "National Catalog prepare batch");
                var result = NationalCatalogDraftExporter.PrepareNextRequestBatch(PluginContext.Operations, options);
                nationalCatalogStatus.Text = $"Статус: пачка {result.BatchNumber}, подготовлено {result.PreparedCount}, всего готово {result.ReadyTotalCount}";
                PluginContext.Log.Info($"Webkassa NKT tab prepared National Catalog request batch. BatchNumber={result.BatchNumber}, Prepared={result.PreparedCount}, ReadyTotal={result.ReadyTotalCount}, NeedsReview={result.NeedsReviewCount}, JsonPath={result.JsonPath}, CsvPath={result.CsvPath}");
                MessageBox.Show(
                    this,
                    $"Пачка подготовлена локально, отправки в National Catalog не было.{Environment.NewLine}Позиции: {result.PreparedCount} из {result.ReadyTotalCount} готовых.{Environment.NewLine}Требует проверки: {result.NeedsReviewCount}.{Environment.NewLine}JSON: {result.JsonPath}{Environment.NewLine}CSV: {result.CsvPath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка пачки";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog request batch preparation failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось подготовить пачку National Catalog: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                prepareButton.Enabled = true;
            }
        }

        private void SubmitNationalCatalogBatch(Button submitButton)
        {
            submitButton.Enabled = false;
            try
            {
                var options = BuildNationalCatalogOptionsFromControls(configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(), "National Catalog submit batch");
                var request = BuildNationalCatalogTestRequest(requireApiKey: true);
                var result = NationalCatalogSyncQueue.SubmitNextBatch(PluginContext.Operations, options, request.ApiKey);
                nationalCatalogStatus.Text = $"Статус: пачка {result.BatchNumber}, отправлено {result.SubmittedCount}, ошибок {result.FailedCount}";
                PluginContext.Log.Info($"Webkassa NKT tab submitted National Catalog batch. DryRun={options.DryRun}, BatchNumber={result.BatchNumber}, Processed={result.ProcessedCount}, Submitted={result.SubmittedCount}, DryRun={result.DryRunCount}, Failed={result.FailedCount}, StatePath={result.StatePath}");
                MessageBox.Show(
                    this,
                    $"Пачка отправлена в National Catalog.{Environment.NewLine}Пачка: {result.BatchNumber}.{Environment.NewLine}Обработано: {result.ProcessedCount}.{Environment.NewLine}Отправлено: {result.SubmittedCount}.{Environment.NewLine}Ошибок: {result.FailedCount}.{Environment.NewLine}State: {result.StatePath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    result.FailedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка отправки пачки";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog batch submit failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось отправить пачку National Catalog: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                submitButton.Enabled = true;
            }
        }

        private void RunNationalCatalogAutoProcessing(Button autoButton)
        {
            autoButton.Enabled = false;
            try
            {
                var options = BuildNationalCatalogOptionsFromControls(configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(), "National Catalog auto batch");
                var request = BuildNationalCatalogTestRequest(requireApiKey: true);
                var result = NationalCatalogSyncQueue.RunAutoProcessing(PluginContext.Operations, options, request.ApiKey);
                nationalCatalogStatus.Text = $"Статус: auto, отправлено {result.SubmittedCount}, ошибок {result.FailedCount}, осталось {result.RemainingCount}";
                PluginContext.Log.Info($"Webkassa NKT tab ran National Catalog auto processing. DryRun={options.DryRun}, LastBatch={result.BatchNumber}, Processed={result.ProcessedCount}, Submitted={result.SubmittedCount}, DryRun={result.DryRunCount}, Failed={result.FailedCount}, Remaining={result.RemainingCount}, StatePath={result.StatePath}");
                MessageBox.Show(
                    this,
                    $"Автообработка завершила текущий лимит.{Environment.NewLine}Последняя пачка: {result.BatchNumber}.{Environment.NewLine}Обработано: {result.ProcessedCount}.{Environment.NewLine}Отправлено: {result.SubmittedCount}.{Environment.NewLine}Ошибок: {result.FailedCount}.{Environment.NewLine}Осталось готовых: {result.RemainingCount}.{Environment.NewLine}State: {result.StatePath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    result.FailedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка автообработки";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog auto processing failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось выполнить автообработку National Catalog: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                autoButton.Enabled = true;
            }
        }

        private void RefreshNationalCatalogStatuses(Button statusButton)
        {
            statusButton.Enabled = false;
            try
            {
                var options = BuildNationalCatalogOptionsFromControls(configuration.NationalCatalog ?? new AdapterNationalCatalogOptions(), "National Catalog status");
                var request = BuildNationalCatalogTestRequest(requireApiKey: true);
                var result = NationalCatalogSyncQueue.RefreshStatuses(options, request.ApiKey);
                nationalCatalogStatus.Text = $"Статус: проверено {result.StatusCheckedCount}, идентификаторов {result.IdentifierReadyCount}";
                PluginContext.Log.Info($"Webkassa NKT tab refreshed National Catalog statuses. Checked={result.StatusCheckedCount}, IdentifierReady={result.IdentifierReadyCount}, StatePath={result.StatePath}");
                MessageBox.Show(
                    this,
                    $"Статусы обновлены.{Environment.NewLine}Проверено заявок: {result.StatusCheckedCount}.{Environment.NewLine}Позиций с GTIN/NTIN/XTIN: {result.IdentifierReadyCount}.{Environment.NewLine}State: {result.StatePath}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка статусов";
                PluginContext.Log.Error($"Webkassa NKT tab National Catalog status refresh failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось обновить статусы National Catalog: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                statusButton.Enabled = true;
            }
        }

        private void BuildWebNktImport(Button importButton)
        {
            importButton.Enabled = false;
            try
            {
                var path = NationalCatalogSyncQueue.BuildWebNktImport();
                nationalCatalogStatus.Text = "Статус: WebNKT import сформирован";
                PluginContext.Log.Info($"Webkassa NKT tab generated WebNKT import file. Path={path}");
                MessageBox.Show(
                    this,
                    $"Файл импорта WebNKT сформирован.{Environment.NewLine}{path}",
                    "Webkassa / НКТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка WebNKT import";
                PluginContext.Log.Error($"Webkassa NKT tab WebNKT import generation failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось сформировать импорт WebNKT: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                importButton.Enabled = true;
            }
        }

        private void TestNationalCatalogConnection(Button testButton)
        {
            testButton.Enabled = false;
            nationalCatalogStatus.Text = "Статус: проверка...";
            try
            {
                var request = BuildNationalCatalogTestRequest();
                var result = NationalCatalogConnectionTester.Test(request);
                if (result.Success)
                {
                    nationalCatalogStatus.Text = $"Статус: API доступен, справочников {result.DictionaryCount}";
                    MessageBox.Show(this, $"OK. National Catalog API доступен. Справочников: {result.DictionaryCount}.", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    nationalCatalogStatus.Text = $"Статус: ошибка {result.Code}";
                    MessageBox.Show(this, $"Ошибка {result.Code}: {result.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка";
                MessageBox.Show(this, $"Ошибка: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                testButton.Enabled = true;
            }
        }

        private void ShowNktIndexStatus(Button statusButton)
        {
            statusButton.Enabled = false;
            try
            {
                var status = NationalCatalogSyncQueue.GetIndexStatus(warmUp: true);
                nationalCatalogStatus.Text = $"Статус: индекс НКТ {(status.IsFresh ? "актуален" : "устарел")}, идентификаторов {status.IdentifierRecordCount}";
                PluginContext.Log.Info($"Webkassa NKT index status requested. Fresh={status.IsFresh}, LoadedInMemory={status.LoadedInMemory}, Records={status.RecordCount}, IdentifierRecords={status.IdentifierRecordCount}, IndexPath={status.IndexPath}");
                MessageBox.Show(
                    this,
                    BuildNktIndexStatusMessage(status),
                    "Webkassa / Индекс НКТ",
                    MessageBoxButtons.OK,
                    status.IsFresh ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception error)
            {
                nationalCatalogStatus.Text = "Статус: ошибка индекса НКТ";
                PluginContext.Log.Error($"Webkassa NKT index status failed. Error={error.GetType().Name}: {error.Message}");
                MessageBox.Show(this, $"Не удалось получить статус индекса НКТ: {error.Message}", "Webkassa / НКТ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                statusButton.Enabled = true;
            }
        }

        private static string BuildNktIndexStatusMessage(NktCatalogIndexStatus status)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Индекс: {(status.IndexExists ? "есть" : "нет")}");
            builder.AppendLine($"Актуален: {(status.IsFresh ? "да" : "нет")}");
            builder.AppendLine($"Загружен в память: {(status.LoadedInMemory ? "да" : "нет")}");
            builder.AppendLine($"Всего записей: {status.RecordCount}");
            builder.AppendLine($"С GTIN/NTIN/XTIN: {status.IdentifierRecordCount}");
            builder.AppendLine($"Lookup по iikoProductId: {status.ProductIdLookupCount}");
            builder.AppendLine($"Lookup по артикулу: {status.NumberLookupCount}");
            builder.AppendLine($"Обновлён: {EmptyDash(status.RebuiltAtLocal)}");
            builder.AppendLine($"Index write UTC: {EmptyDash(status.IndexWriteTimeUtc)}");
            builder.AppendLine($"Queue write UTC: {EmptyDash(status.CurrentSourceStateWriteTimeUtc)}");
            builder.AppendLine();
            builder.AppendLine($"Index: {status.IndexPath}");
            builder.AppendLine($"Queue: {status.SourceStatePath}");
            return builder.ToString();
        }

        private static string EmptyDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value ?? "-";
        }

        private void TestConnection(Button testButton)
        {
            testButton.Enabled = false;
            connectionStatus.Text = "Статус: проверка...";
            try
            {
                var request = BuildConnectionTestRequest();
                var result = WebkassaConnectionTester.Test(request);
                if (result.Success)
                {
                    connectionStatus.Text = $"Статус: подключено, касса {result.CashboxStatus ?? "-"}";
                    MessageBox.Show(this, result.Message, "Webkassa", MessageBoxButtons.OK, result.LicenseWarning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
                else
                {
                    connectionStatus.Text = $"Статус: ошибка {result.Code}";
                    MessageBox.Show(this, $"Ошибка {result.Code}: {result.Message}", "Webkassa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception error)
            {
                connectionStatus.Text = "Статус: ошибка";
                MessageBox.Show(this, $"Ошибка: {error.Message}", "Webkassa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                testButton.Enabled = true;
            }
        }

        private void BringDialogToFront()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            TopMost = true;
            BringToFront();
            Activate();

            if (ownerHandle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(Handle);
        }

        private AdapterNationalCatalogOptions BuildNationalCatalogOptionsFromControls(AdapterNationalCatalogOptions existing, string secretPrefix)
        {
            existing = existing ?? new AdapterNationalCatalogOptions();
            var existingSecretRefs = existing.SecretRefs ?? new AdapterSecretReferences();
            var existingAutoFill = existing.AutoFill ?? new AdapterNationalCatalogAutoFillOptions();
            return new AdapterNationalCatalogOptions
            {
                Enabled = nationalCatalogEnabled.Checked,
                BaseUrl = FirstNonEmpty(nationalCatalogBaseUrl.Text.Trim(), "https://nationalcatalog.kz/gwp"),
                DryRun = nationalCatalogDryRun.Checked,
                BatchSize = (int)nationalCatalogBatchSize.Value,
                AutoBatchLimit = (int)nationalCatalogAutoBatchLimit.Value,
                AutoDelaySeconds = (int)nationalCatalogAutoDelaySeconds.Value,
                SecretRefs = new AdapterSecretReferences
                {
                    ApiKey = FirstNonEmpty(existingSecretRefs.ApiKey, $"{secretPrefix} api key"),
                    Login = FirstNonEmpty(existingSecretRefs.Login, $"{secretPrefix} login"),
                    Password = FirstNonEmpty(existingSecretRefs.Password, $"{secretPrefix} password"),
                },
                AutoFill = new AdapterNationalCatalogAutoFillOptions
                {
                    Enabled = true,
                    TreatDishAsOwnProduction = nationalCatalogTreatDishAsOwnProduction.Checked,
                    TreatGoodsWithoutBarcodeAsOwnProduction = nationalCatalogTreatGoodsWithoutBarcodeAsOwnProduction.Checked,
                    CountryCode = FirstNonEmpty(existingAutoFill.CountryCode, "KZ"),
                    CountryName = FirstNonEmpty(nationalCatalogCountryName.Text.Trim(), "Казахстан"),
                    ProducerName = nationalCatalogProducerName.Text.Trim(),
                    ProducerTin = nationalCatalogProducerTin.Text.Trim(),
                    Brand = nationalCatalogBrand.Text.Trim(),
                    DefaultOktru = nationalCatalogDefaultOktru.Text.Trim(),
                    DefaultMeasureCode = existingAutoFill.DefaultMeasureCode,
                    DefaultMeasureName = FirstNonEmpty(nationalCatalogDefaultMeasureName.Text.Trim(), "порция"),
                    DefaultQuantity = existingAutoFill.DefaultQuantity <= 0m ? 1m : existingAutoFill.DefaultQuantity,
                    AutoPublication = nationalCatalogAutoPublication.Checked,
                    Rules = existingAutoFill.Rules ?? new List<AdapterNationalCatalogAutoFillRule>()
                }
            };
        }

        private ConnectionTestRequest BuildConnectionTestRequest()
        {
            var mode = SelectedValue(authMode);
            var existingApiKeyRef = configuration.SecretRefs?.ApiKey ?? string.Empty;
            var existingPasswordRef = configuration.SecretRefs?.Password ?? string.Empty;
            var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
            var resolvedApiKey = string.Empty;
            var resolvedPassword = password.Text;

            if (mode != AdapterAuthOptions.LoginPasswordOnlyMode)
            {
                resolvedApiKey = string.IsNullOrWhiteSpace(apiKey.Text)
                    ? ResolveSecretBestEffort(provider, existingApiKeyRef, "api key")
                    : apiKey.Text.Trim();
            }

            if (string.IsNullOrEmpty(resolvedPassword))
                resolvedPassword = ResolveSecretBestEffort(provider, existingPasswordRef, "password");

            if (string.IsNullOrWhiteSpace(baseUrl.Text))
                throw new InvalidOperationException("Base URL is required.");
            if (mode != AdapterAuthOptions.LoginPasswordOnlyMode && string.IsNullOrWhiteSpace(resolvedApiKey))
                throw new InvalidOperationException("API key is required for this auth mode.");
            if (string.IsNullOrWhiteSpace(login.Text))
                throw new InvalidOperationException("Login is required.");
            if (string.IsNullOrEmpty(resolvedPassword))
                throw new InvalidOperationException("Password is required.");
            if (string.IsNullOrWhiteSpace(cashboxUniqueNumber.Text))
                throw new InvalidOperationException("CashboxUniqueNumber is required.");

            return new ConnectionTestRequest
            {
                BaseUrl = baseUrl.Text.Trim(),
                ApiKey = resolvedApiKey,
                Login = login.Text.Trim(),
                Password = resolvedPassword,
                CashboxUniqueNumber = cashboxUniqueNumber.Text.Trim()
            };
        }

        private NationalCatalogTestRequest BuildNationalCatalogTestRequest(bool requireApiKey = true)
        {
            var provider = new DpapiFileSecretProvider(scope: DataProtectionScope.LocalMachine);
            var existingApiKeyRef = configuration.NationalCatalog?.SecretRefs?.ApiKey ?? string.Empty;
            var resolvedApiKey = string.IsNullOrWhiteSpace(nationalCatalogApiKey.Text)
                ? ResolveSecretBestEffort(provider, existingApiKeyRef, "national catalog api key")
                : nationalCatalogApiKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(nationalCatalogBaseUrl.Text))
                throw new InvalidOperationException("National Catalog URL is required.");
            if (requireApiKey && string.IsNullOrWhiteSpace(resolvedApiKey))
                throw new InvalidOperationException("National Catalog API key is required.");

            return new NationalCatalogTestRequest
            {
                BaseUrl = nationalCatalogBaseUrl.Text.Trim(),
                ApiKey = resolvedApiKey
            };
        }

        private void UpdateApiKeyState()
        {
            apiKey.Enabled = SelectedValue(authMode) != AdapterAuthOptions.LoginPasswordOnlyMode;
        }

        private static void SelectCombo(ComboBox comboBox, string value)
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is ComboItem item &&
                    string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private static string SelectedValue(ComboBox comboBox)
        {
            return comboBox.SelectedItem is ComboItem item ? item.Value : string.Empty;
        }

        private static int ParsePaperKind(string value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static string ResolveSecretBestEffort(DpapiFileSecretProvider provider, string? secretRef, string purpose)
        {
            if (string.IsNullOrWhiteSpace(secretRef))
                return string.Empty;

            var result = provider.Resolve(secretRef!, purpose);
            return result.Success ? result.Value ?? string.Empty : string.Empty;
        }

        private static string SecretRefForSave(string existingRef, string baseRef, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return existingRef ?? string.Empty;

            var safeBaseRef = string.IsNullOrWhiteSpace(baseRef) ? "Webkassa secret" : baseRef.Trim();
            return $"{safeBaseRef} {DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }

            return string.Empty;
        }
    }

    private sealed class WindowHandle : IWin32Window
    {
        public WindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }

    private static void ShowTopMostMessage(string text, string caption, MessageBoxIcon icon)
    {
        using (var form = new Form { TopMost = true, ShowInTaskbar = false, Size = new Size(1, 1), StartPosition = FormStartPosition.Manual })
        {
            form.Load += (_, _) => form.Location = new Point(-2000, -2000);
            form.Show();
            MessageBox.Show(form, text, caption, MessageBoxButtons.OK, icon);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    private sealed class ComboItem
    {
        public ComboItem(string text, string value)
        {
            Text = text;
            Value = value;
        }

        public string Text { get; }

        public string Value { get; }

        public override string ToString()
        {
            return Text;
        }
    }

    private sealed class ConnectionTestRequest
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string Login { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string CashboxUniqueNumber { get; set; } = string.Empty;
    }

    private sealed class ConnectionTestResult
    {
        public bool Success { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? CashboxStatus { get; set; }

        public string? LicenseExpirationDate { get; set; }

        public bool LicenseWarning { get; set; }
    }

    private sealed class NationalCatalogTestRequest
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;
    }

    private sealed class NationalCatalogTestResult
    {
        public bool Success { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public int DictionaryCount { get; set; }
    }

    private static class NationalCatalogConnectionTester
    {
        public static NationalCatalogTestResult Test(NationalCatalogTestRequest request)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-KEY", request.ApiKey);

                try
                {
                    var url = $"{request.BaseUrl.TrimEnd('/')}/portal/api/v1/dictionaries";
                    var response = client.GetAsync(url).GetAwaiter().GetResult();
                    var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        return new NationalCatalogTestResult
                        {
                            Success = false,
                            Code = ((int)response.StatusCode).ToString(),
                            Message = FirstNonEmpty(TryReadApiErrorMessage(responseText), response.ReasonPhrase, "National Catalog API error.")
                        };
                    }

                    var dictionaries = DeserializeNationalCatalogDictionaries(responseText);
                    return new NationalCatalogTestResult
                    {
                        Success = true,
                        Code = "OK",
                        Message = "Connected.",
                        DictionaryCount = dictionaries.Length
                    };
                }
                catch (HttpRequestException error)
                {
                    return new NationalCatalogTestResult { Success = false, Code = "NETWORK", Message = error.Message };
                }
                catch (TaskCanceledException)
                {
                    return new NationalCatalogTestResult { Success = false, Code = "TIMEOUT", Message = "Connection timed out." };
                }
                catch (Exception error)
                {
                    return new NationalCatalogTestResult { Success = false, Code = "CLIENT", Message = error.Message };
                }
            }
        }

        private static PortalAttributeObjectDto[] DeserializeNationalCatalogDictionaries(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "[]" : json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(PortalAttributeObjectDto[]));
                return serializer.ReadObject(stream) as PortalAttributeObjectDto[] ?? new PortalAttributeObjectDto[0];
            }
        }

        private static string TryReadApiErrorMessage(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(NationalCatalogApiError));
                    var value = serializer.ReadObject(stream) as NationalCatalogApiError;
                    return FirstNonEmpty(value?.Message, value?.Code);
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static class WebkassaConnectionTester
    {
        public static ConnectionTestResult Test(ConnectionTestRequest request)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                if (!string.IsNullOrWhiteSpace(request.ApiKey))
                    client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", request.ApiKey);

                var authorize = PostJson<AuthorizeRequest, AuthorizeData>(
                    client,
                    request.BaseUrl,
                    "/api/v4/Authorize",
                    new AuthorizeRequest { Login = request.Login, Password = request.Password });

                if (!authorize.Success)
                    return authorize.ToConnectionResult("AUTHORIZE");

                if (authorize.Data == null || string.IsNullOrWhiteSpace(authorize.Data.Token))
                {
                    return new ConnectionTestResult
                    {
                        Success = false,
                        Code = "AUTHORIZE_NO_TOKEN",
                        Message = "Webkassa Authorize did not return token."
                    };
                }

                var clientInfo = PostJson<ClientInfoRequest, ClientInfoData>(
                    client,
                    request.BaseUrl,
                    "/api-portal/v4/cashbox/client-info",
                    new ClientInfoRequest
                    {
                        Token = authorize.Data.Token!,
                        CashboxUniqueNumber = request.CashboxUniqueNumber
                    });

                if (!clientInfo.Success)
                    return clientInfo.ToConnectionResult("CASHBOX");

                if (clientInfo.Data == null)
                {
                    return new ConnectionTestResult
                    {
                        Success = false,
                        Code = "CASHBOX_NO_DATA",
                        Message = "Webkassa cashbox client-info returned no data."
                    };
                }

                return new ConnectionTestResult
                {
                    Success = true,
                    Code = "OK",
                    Message = BuildSuccessMessage(clientInfo.Data),
                    CashboxStatus = clientInfo.Data.CashboxStatus?.ToString(),
                    LicenseExpirationDate = clientInfo.Data.License?.LicenseExpirationDate,
                    LicenseWarning = IsLicenseWarning(clientInfo.Data.License?.LicenseExpirationDate)
                };
            }
        }

        private static string BuildSuccessMessage(ClientInfoData clientInfo)
        {
            var lines = new List<string>
            {
                "OK. Подключено к Webkassa, заводской номер кассы проверен.",
                $"Статус кассы: {clientInfo.CashboxStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"}"
            };

            var licenseExpirationDate = clientInfo.License?.LicenseExpirationDate;
            if (!string.IsNullOrWhiteSpace(licenseExpirationDate))
            {
                lines.Add($"Лицензия до: {licenseExpirationDate}");
                if (IsLicenseWarning(licenseExpirationDate))
                    lines.Add("Внимание: срок лицензии Webkassa меньше 7 дней. Продлите лицензию.");
            }

            var ofdExpirationDate = clientInfo.Ofd?.Expiration;
            if (!string.IsNullOrWhiteSpace(ofdExpirationDate))
                lines.Add($"ОФД до: {ofdExpirationDate}");

            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsLicenseWarning(string? expirationDate)
        {
            if (string.IsNullOrWhiteSpace(expirationDate))
                return false;

            if (!DateTimeOffset.TryParse(expirationDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var expiration))
                return false;

            var remaining = expiration - DateTimeOffset.Now;
            return remaining.TotalMilliseconds < 7 * 24 * 60 * 60 * 1000;
        }

        private static WebkassaEnvelope<TResponse> PostJson<TRequest, TResponse>(
            HttpClient client,
            string baseUrl,
            string path,
            TRequest request)
        {
            try
            {
                var url = $"{baseUrl.TrimEnd('/')}{path}";
                var body = SerializeJson(request);
                var response = client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var envelope = DeserializeJson<TRequest, TResponse>(
                    responseText,
                    request,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? string.Empty);
                envelope.HttpStatusCode = (int)response.StatusCode;
                envelope.HttpReasonPhrase = response.ReasonPhrase ?? string.Empty;
                if (!response.IsSuccessStatusCode && envelope.Errors == null)
                {
                    envelope.Errors = new[]
                    {
                        new WebkassaError { Code = ((int)response.StatusCode).ToString(), Text = response.ReasonPhrase }
                    };
                }
                return envelope;
            }
            catch (HttpRequestException error)
            {
                return WebkassaEnvelope<TResponse>.Failed("NETWORK", error.Message);
            }
            catch (TaskCanceledException)
            {
                return WebkassaEnvelope<TResponse>.Failed("TIMEOUT", "Connection timed out.");
            }
            catch (Exception error)
            {
                return WebkassaEnvelope<TResponse>.Failed("CLIENT", error.Message);
            }
        }

        private static string SerializeJson<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static WebkassaEnvelope<TResponse> DeserializeJson<TRequest, TResponse>(
            string json,
            TRequest request,
            int httpStatusCode,
            string httpReasonPhrase)
        {
            if (string.IsNullOrWhiteSpace(json))
                return WebkassaEnvelope<TResponse>.Failed("EMPTY_RESPONSE", $"Webkassa вернула пустой ответ ({DescribeHttpStatus(httpStatusCode, httpReasonPhrase)}).");

            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
                return WebkassaEnvelope<TResponse>.Failed("NON_JSON_RESPONSE", BuildUnexpectedResponseMessage(json, request, httpStatusCode, httpReasonPhrase));

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(WebkassaEnvelope<TResponse>));
                    var value = serializer.ReadObject(stream) as WebkassaEnvelope<TResponse>;
                    return value ?? WebkassaEnvelope<TResponse>.Failed("EMPTY_RESPONSE", "Empty JSON response.");
                }
            }
            catch (SerializationException error)
            {
                return WebkassaEnvelope<TResponse>.Failed(
                    "INVALID_JSON_RESPONSE",
                    $"{BuildUnexpectedResponseMessage(json, request, httpStatusCode, httpReasonPhrase)} Ошибка разбора: {error.Message}");
            }
        }

        private static string BuildUnexpectedResponseMessage<TRequest>(string responseText, TRequest request, int httpStatusCode, string httpReasonPhrase)
        {
            var preview = RedactResponsePreview(responseText, request);
            return $"Webkassa вернула ответ не в формате API v4 JSON ({DescribeHttpStatus(httpStatusCode, httpReasonPhrase)}): {preview}. Проверьте Base URL, режим авторизации, логин/пароль и доступность этого аккаунта для API.";
        }

        private static string DescribeHttpStatus(int httpStatusCode, string httpReasonPhrase)
        {
            var reason = string.IsNullOrWhiteSpace(httpReasonPhrase) ? string.Empty : $" {httpReasonPhrase.Trim()}";
            return $"HTTP {httpStatusCode.ToString(CultureInfo.InvariantCulture)}{reason}";
        }

        private static string RedactResponsePreview<TRequest>(string responseText, TRequest request)
        {
            var preview = OneLine(responseText);
            if (request is AuthorizeRequest authorize)
            {
                preview = RedactValue(preview, authorize.Login);
                preview = RedactValue(preview, authorize.Password);
            }
            else if (request is ClientInfoRequest clientInfo)
            {
                preview = RedactValue(preview, clientInfo.Token);
            }

            if (preview.Length > 300)
                preview = preview.Substring(0, 300) + "...";
            return string.IsNullOrWhiteSpace(preview) ? "(пустой ответ)" : preview;
        }

        private static string OneLine(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
        }

        private static string RedactValue(string text, string? value)
        {
            return string.IsNullOrEmpty(value) ? text : text.Replace(value, "__REDACTED__");
        }
    }

    [DataContract]
    private sealed class WebkassaEnvelope<T>
    {
        [DataMember(Name = "Data")]
        public T? Data { get; set; }

        [DataMember(Name = "Errors")]
        public WebkassaError[]? Errors { get; set; }

        public int HttpStatusCode { get; set; }

        public string HttpReasonPhrase { get; set; } = string.Empty;

        public bool Success => (HttpStatusCode == 0 || (HttpStatusCode >= 200 && HttpStatusCode <= 299)) && (Errors == null || Errors.Length == 0);

        public static WebkassaEnvelope<T> Failed(string code, string message)
        {
            return new WebkassaEnvelope<T>
            {
                Errors = new[] { new WebkassaError { Code = code, Text = message } }
            };
        }

        public ConnectionTestResult ToConnectionResult(string stage)
        {
            var error = Errors != null && Errors.Length > 0 ? Errors[0] : null;
            var code = FirstNonEmpty(error?.Code, error?.ErrorCode, HttpStatusCode > 0 ? HttpStatusCode.ToString() : stage);
            var message = FirstNonEmpty(error?.Text, error?.Message, HttpReasonPhrase, "Unknown Webkassa error.");
            return new ConnectionTestResult
            {
                Success = false,
                Code = $"{stage}_{code}",
                Message = message
            };
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return string.Empty;
    }

    [DataContract]
    private sealed class WebkassaError
    {
        [DataMember(Name = "Code")]
        public string? Code { get; set; }

        [DataMember(Name = "ErrorCode")]
        public string? ErrorCode { get; set; }

        [DataMember(Name = "Text")]
        public string? Text { get; set; }

        [DataMember(Name = "Message")]
        public string? Message { get; set; }
    }

    [DataContract]
    private sealed class PortalAttributeObjectDto
    {
        [DataMember(Name = "code")]
        public string? Code { get; set; }

        [DataMember(Name = "nameRu")]
        public string? NameRu { get; set; }
    }

    [DataContract]
    private sealed class NationalCatalogApiError
    {
        [DataMember(Name = "code")]
        public string? Code { get; set; }

        [DataMember(Name = "message")]
        public string? Message { get; set; }
    }

    [DataContract]
    private sealed class AuthorizeRequest
    {
        [DataMember(Name = "Login")]
        public string Login { get; set; } = string.Empty;

        [DataMember(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class AuthorizeData
    {
        [DataMember(Name = "Token")]
        public string? Token { get; set; }
    }

    [DataContract]
    private sealed class ClientInfoRequest
    {
        [DataMember(Name = "Token")]
        public string Token { get; set; } = string.Empty;

        [DataMember(Name = "CashboxUniqueNumber")]
        public string CashboxUniqueNumber { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class ClientInfoData
    {
        [DataMember(Name = "CashboxStatus")]
        public int? CashboxStatus { get; set; }

        [DataMember(Name = "License")]
        public ClientInfoLicense? License { get; set; }

        [DataMember(Name = "Ofd")]
        public ClientInfoOfd? Ofd { get; set; }
    }

    [DataContract]
    private sealed class ClientInfoLicense
    {
        [DataMember(Name = "LicenseStatus")]
        public int? LicenseStatus { get; set; }

        [DataMember(Name = "LicenseExpirationDate")]
        public string? LicenseExpirationDate { get; set; }
    }

    [DataContract]
    private sealed class ClientInfoOfd
    {
        [DataMember(Name = "Ofd")]
        public int? Ofd { get; set; }

        [DataMember(Name = "Expiration")]
        public string? Expiration { get; set; }
    }
}
