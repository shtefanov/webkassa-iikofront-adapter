using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Resto.Front.Api.Webkassa.V9;

public static class WebkassaTicketQrDialog
{
    public static bool IsSafeExternalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               !uri.IsLoopback &&
               uri.HostNameType == UriHostNameType.Dns &&
               uri.IsDefaultPort;
    }

    public static void Show(string externalUrl, string? checkNumber)
    {
        if (!IsSafeExternalUrl(externalUrl))
            throw new InvalidOperationException("Webkassa вернула недопустимую внешнюю ссылку чека.");

        var normalizedUrl = externalUrl.Trim();
        var ownerHandle = NativeMethods.GetForegroundWindow();
        var thread = new Thread(() => ShowDialog(normalizedUrl, checkNumber, ownerHandle));
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
    }

    private static void ShowDialog(string externalUrl, string? checkNumber, IntPtr ownerHandle)
    {
        try
        {
            Application.EnableVisualStyles();
            using (var form = new TicketQrForm(externalUrl, checkNumber))
            {
                if (ownerHandle == IntPtr.Zero)
                    form.ShowDialog();
                else
                    form.ShowDialog(new WindowHandle(ownerHandle));
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(
                error.Message,
                "Ошибка QR Webkassa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);
        }
    }

    private sealed class TicketQrForm : Form
    {
        private readonly string externalUrl;
        private readonly PictureBox qrPicture = new PictureBox();

        public TicketQrForm(string externalUrl, string? checkNumber)
        {
            this.externalUrl = externalUrl;
            Text = string.IsNullOrWhiteSpace(checkNumber)
                ? "QR Webkassa"
                : $"QR Webkassa — чек {checkNumber}";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 600);
            Font = new Font("Segoe UI", 10F);

            var title = new Label
            {
                Text = "Внешняя ссылка для просмотра чека",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            };

            qrPicture.Dock = DockStyle.Fill;
            qrPicture.SizeMode = PictureBoxSizeMode.CenterImage;
            qrPicture.Image = QrCodeRenderer.Render(externalUrl, 360);

            var link = new TextBox
            {
                Text = externalUrl,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
            };

            var copy = new Button { Text = "Копировать ссылку", AutoSize = true };
            copy.Click += (_, _) => CopyLink();
            var open = new Button { Text = "Открыть ссылку", AutoSize = true };
            open.Click += (_, _) => OpenLink();
            var close = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.OK };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(copy);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(18),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(qrPicture, 0, 1);
            layout.Controls.Add(link, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);

            AcceptButton = close;
            CancelButton = close;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                qrPicture.Image?.Dispose();
            base.Dispose(disposing);
        }

        private void CopyLink()
        {
            try
            {
                Clipboard.SetText(externalUrl);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Не удалось скопировать ссылку", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = externalUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Не удалось открыть ссылку", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
