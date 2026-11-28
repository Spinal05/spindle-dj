using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    public class MainForm : Form
    {
        readonly Panel sidebar = new Panel();
        readonly Panel host = new Panel();
        readonly List<NavButton> nav = new List<NavButton>();
        public readonly DevicesPage Devices;
        public readonly ScanPage Scan;
        public readonly PartitionPage Partitions;
        public readonly BackupPage Backup;
        public readonly AboutPage About;
        Control current;

        public static MainForm Instance { get; private set; }

        public MainForm(string[] args = null)
        {
            Instance = this;
            Text = "Spindle Recovery";
            Font = Theme.Body;
            BackColor = Theme.Background;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.S(1100), Theme.S(720));
            Size = new Size(Theme.S(1280), Theme.S(800));
            StartPosition = FormStartPosition.CenterScreen;
            try
            {
                using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
                    if (s != null) Icon = new Icon(s);
            }
            catch { }

            sidebar.Dock = DockStyle.Left;
            sidebar.Width = Theme.S(232);
            sidebar.BackColor = Theme.Sidebar;
            sidebar.Paint += PaintSidebar;

            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Background;
            host.Padding = new Padding(0);

            Controls.Add(host);
            Controls.Add(sidebar);

            Devices = new DevicesPage();
            Scan = new ScanPage();
            Partitions = new PartitionPage();
            Backup = new BackupPage();
            About = new AboutPage();

            AddNav("Data Recovery", Icons.Kind.Recover, () => Show(Scan.HasSession && Scan.Visible ? (Control)Scan : Devices));
            AddNav("Partition Recovery", Icons.Kind.Partition, () => Show(Partitions));
            AddNav("Byte-to-byte Backup", Icons.Kind.Backup, () => Show(Backup));
            AddNav("Recovery Vault", Icons.Kind.Vault, OpenVault);
            AddNav("About", Icons.Kind.About, () => Show(About));

            Show(Devices);
            Shown += (s, e) =>
            {
                Devices.RefreshDevices();
                // Disk images given on the command line (or via "Open with") are attached right away.
                if (args != null)
                    foreach (var a in args)
                        if (System.IO.File.Exists(a)) Devices.AttachImage(a);
            };
            // Drop disk image files onto the window to attach them.
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
                Show(Devices);
                foreach (var f in files) if (System.IO.File.Exists(f)) Devices.AttachImage(f);
            };
            FormClosing += (s, e) =>
            {
                if (Scan.IsBusy && MessageBox.Show(this, "A scan is still running. Stop it and quit?", "Spindle Recovery",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    e.Cancel = true;
                else Scan.StopForExit();
            };
        }

        void AddNav(string text, Icons.Kind icon, Action onClick)
        {
            var b = new NavButton(text, icon) { Width = sidebar.Width, Left = 0, Top = Theme.S(96) + nav.Count * Theme.S(46) };
            b.Click += (s, e) =>
            {
                onClick();
            };
            nav.Add(b);
            sidebar.Controls.Add(b);
        }

        void PaintSidebar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            Icons.DrawLogo(g, new RectangleF(Theme.S(22), Theme.S(28), Theme.S(36), Theme.S(36)));
            using (var b = new SolidBrush(Color.White))
                g.DrawString("Spindle", Theme.Font(13f, FontStyle.Bold), b, Theme.S(66), Theme.S(24));
            using (var b = new SolidBrush(Color.FromArgb(147, 197, 253)))
                g.DrawString("RECOVERY", Theme.Font(8f, FontStyle.Bold), b, Theme.S(68), Theme.S(48));
            using (var b = new SolidBrush(Color.FromArgb(110, 118, 140)))
            {
                var text = "Powered by TestDisk & PhotoRec 7.2";
                g.DrawString(text, Theme.Font(7.5f), b, Theme.S(22), sidebar.Height - Theme.S(36));
            }
        }

        public void Show(Control page)
        {
            if (current == page) return;
            host.SuspendLayout();
            if (current != null) host.Controls.Remove(current);
            page.Dock = DockStyle.Fill;
            host.Controls.Add(page);
            current = page;
            host.ResumeLayout();
            (page as PageBase)?.OnPageShown();
            int idx = page == Devices || page == Scan ? 0 : page == Partitions ? 1 : page == Backup ? 2 : page == About ? 4 : -1;
            for (int i = 0; i < nav.Count; i++) nav[i].Active = i == idx;
        }

        void OpenVault()
        {
            var dir = Scan.CurrentVault;
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) dir = ScanOptionsDialog.DefaultVaultRoot;
            System.IO.Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        public static void OpenFolder(string dir)
        {
            try
            {
                if (EngineHost.IsWindows) System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                else System.Diagnostics.Process.Start("xdg-open", dir);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Spindle Recovery"); }
        }

        public static void Error(IWin32Window owner, string what, Exception ex)
        {
            MessageBox.Show(owner, what + "\n\n" + ex.Message, "Spindle Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
