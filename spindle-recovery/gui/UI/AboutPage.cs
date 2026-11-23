using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    public class AboutPage : PageBase
    {
        public AboutPage() : base("About", "Spindle Recovery " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3))
        {
            var card = new Card { Dock = DockStyle.Top, Height = Theme.S(420) };
            var logo = new Panel { Location = new Point(Theme.S(26), Theme.S(26)), Size = new Size(Theme.S(64), Theme.S(64)), BackColor = Color.White };
            logo.Paint += (s, e) => { Theme.HighQuality(e.Graphics); Icons.DrawLogo(e.Graphics, new RectangleF(0, 0, logo.Width - 1, logo.Height - 1)); };
            var name = MakeLabel("Spindle Recovery", Theme.Big, Theme.Text);
            name.BackColor = Color.White;
            name.Location = new Point(Theme.S(104), Theme.S(28));
            var tag = MakeLabel("Graphical data and partition recovery for Windows", Theme.Subtitle, Theme.TextMuted);
            tag.BackColor = Color.White;
            tag.Location = new Point(Theme.S(106), Theme.S(70));
            var text = new WrapLabel
            {
                BackColor = Color.White,
                Font = Theme.Body,
                ForeColor = Theme.Text,
                Location = new Point(Theme.S(26), Theme.S(120)),
                Text =
                    "Recovery engines: TestDisk 7.2 and PhotoRec 7.2 by Christophe GRENIER (https://www.cgsecurity.org), " +
                    "compiled from the official 7.2 source with small changes so this interface can follow their progress " +
                    "(see engine/patches in the source repository).\n\n" +
                    "What each tool does here:\n" +
                    "  •  Quick Scan — TestDisk reads FAT/exFAT directory tables and lists deleted files with their original names and folders.\n" +
                    "  •  Deep Scan — PhotoRec ignores the file system and rebuilds 480+ file formats from their content signatures.\n" +
                    "  •  Partition Recovery — TestDisk finds lost partitions and rewrites the partition table.\n" +
                    "  •  Byte-to-byte Backup — images a drive so recovery can work on a copy.\n\n" +
                    "Spindle Recovery is free software released under the GNU General Public License version 2 or (at your option) any later version, " +
                    "the same licence as TestDisk and PhotoRec. It comes with ABSOLUTELY NO WARRANTY. " +
                    "It is an independent project and is not affiliated with CGSecurity or with any commercial data-recovery vendor.\n\n" +
                    "Engine folder: " + SafeEngineDir() + "\nScan logs: " + SafeSessions(),
            };
            card.Controls.AddRange(new Control[] { logo, name, tag, text });
            card.Resize += (s, e) => text.Width = card.Width - Theme.S(52);
            Body.Controls.Add(card);
        }

        static string SafeEngineDir() { try { return EngineHost.EngineDir; } catch { return "(unavailable)"; } }
        static string SafeSessions() { try { return EngineHost.SessionsDir; } catch { return "(unavailable)"; } }
    }
}
