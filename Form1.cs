using LibHac;
using Newtonsoft.Json;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using VGAudio.Formats;

namespace SwitchExplorer
{
    public partial class Form1 : Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        public Nca Nca { get; set; }
        public Nca Patch { get; set; }
        public Nca Control { get; set; }
        public Romfs Rom { get; set; }
        public static SoundPlayer Audio { get; set; }
        public IAudioFormat SoundFile { get; set; }

        public Form1() => InitializeComponent();

        public bool CheckExtension(string Target, string Extension)
        {
            if (Regex.Match(Extension, $"(?i){Target}").Success) return true;
            else return false;
        }

        private string[] GetTitleMeta(string TitleID = null)
        {
            var Info = new string[]
            {
                "Title missing from database",
                ""
            };
            if (Control != null)
            {
                var Rom = new Romfs(Control.OpenSection(0, false, IntegrityCheckLevel.None));
                var OpenControl = Rom.OpenFile(Rom.Files.FirstOrDefault(f => f.Name == "control.nacp"));
                var OpenIcon = Rom.OpenFile(Rom.Files.FirstOrDefault(f => f.Name.Contains("icon")));
                var ControlNacp = new Nacp(new BinaryReader(OpenControl));
                var Lang = ControlNacp.Languages.FirstOrDefault(l => l.Title.Length > 1);
                Info[0] = Lang.Title;
                Info[1] = Lang.Developer;
                pictureBox1.Image = Image.FromStream(OpenIcon);
            }
            else
            {
                using (var WC = new WebClient())
                {
                    var Cli = WC.DownloadData($"https://gamechat.network/nucleus?title_id={TitleID}");
                    pictureBox1.Image = Image.FromStream(new MemoryStream(Cli));

                    Info[0] = Encoding.UTF8.GetString
                    (
                        WC.ResponseHeaders.Get("X-GCN-Game-Name")
                        .Select(b => (byte)b).ToArray()
                    );

                    Info[1] = Encoding.UTF8.GetString
                    (
                        WC.ResponseHeaders.Get("X-GCN-Game-Dev")
                        .Select(b => (byte)b).ToArray()
                    );
                }
            }
            return Info;
        }

        private void Open()
        {
            treeView1.Nodes.Clear();

            string FileToOpen = null;

            if (Program.FileArg != null) FileToOpen = Program.FileArg;
            else FileToOpen = openFileDialog1.FileName;

            Program.FileArg = null;
            Stream Input = null;

            try
            {
                string ExpEnv(string In) => Environment.ExpandEnvironmentVariables(In);

                var ProdKeys = ExpEnv(@"%USERPROFILE%\.switch\prod.keys");
                var TitleKeys = ExpEnv(@"%USERPROFILE%\.switch\title.keys");

                var Keys = ExternalKeys.ReadKeyFile(ProdKeys, TitleKeys);

                var Ext = (new FileInfo(FileToOpen).Extension);

                if (Ext == ".nsp")
                {
                    var InputPFS = File.OpenRead(FileToOpen);
                    var Pfs = new Pfs(InputPFS);
                    var CnmtNca = new Nca(Keys, Pfs.OpenFile(Pfs.Files.FirstOrDefault(s => s.Name.Contains(".cnmt.nca"))), false);
                    var CnmtPfs = new Pfs(CnmtNca.OpenSection(0, false, IntegrityCheckLevel.None));
                    var Cnmt = new Cnmt(CnmtPfs.OpenFile(CnmtPfs.Files[0]));
                    var Program = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Program);
                    var CtrlEntry = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Control);
                    Control = new Nca(Keys, Pfs.OpenFile($"{CtrlEntry.NcaId.ToHexString().ToLower()}.nca"), false);
                    Input = Pfs.OpenFile($"{Program.NcaId.ToHexString().ToLower()}.nca");
                }
                else if (Ext == ".xci")
                {
                    var InputPFS = File.OpenRead(FileToOpen);
                    var Xci = new Xci(Keys, InputPFS);
                    var CnmtNca = new Nca(Keys, Xci.SecurePartition.OpenFile(Xci.SecurePartition.Files.FirstOrDefault(s => s.Name.Contains(".cnmt.nca"))), false);
                    var CnmtPfs = new Pfs(CnmtNca.OpenSection(0, false, IntegrityCheckLevel.None));
                    var Cnmt = new Cnmt(CnmtPfs.OpenFile(CnmtPfs.Files[0]));
                    var Program = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Program);
                    var CtrlEntry = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Control);
                    Control = new Nca(Keys, Xci.SecurePartition.OpenFile($"{CtrlEntry.NcaId.ToHexString().ToLower()}.nca"), false);
                    Input = Xci.SecurePartition.OpenFile($"{Program.NcaId.ToHexString().ToLower()}.nca");
                }
                else if (FileToOpen.Split('.')[1] == "cnmt" && Ext == ".nca")
                {
                    var TargetFile = File.OpenRead(FileToOpen);
                    var CnmtNca = new Nca(Keys, TargetFile, false);
                    var CnmtPfs = new Pfs(CnmtNca.OpenSection(0, false, IntegrityCheckLevel.None));
                    var Cnmt = new Cnmt(CnmtPfs.OpenFile(CnmtPfs.Files[0]));
                    var Program = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Program);
                    var CtrlEntry = Cnmt.ContentEntries.FirstOrDefault(c => c.Type == CnmtContentType.Control);
                    Control = new Nca(Keys, File.OpenRead($"{CtrlEntry.NcaId.ToHexString().ToLower()}.nca"), false);
                    Input = File.OpenRead($"{Program.NcaId.ToHexString().ToLower()}.nca");
                }
                else Input = File.OpenRead(FileToOpen);

                try
                {
                    Nca = new Nca(Keys, Input, true);

                    if (Nca.HasRightsId && !Keys.TitleKeys.Keys.Any(k => k.SequenceEqual(Nca.Header.RightsId)))
                        MessageBox.Show($"Error: the titlekey for {Nca.Header.RightsId.ToHexString().ToLower()} is not present in your key file.");
                    else
                    {
                        bool isUpdateNca = false;

                        if (Nca.Sections.Any(s => s?.Type == SectionType.Bktr))
                            isUpdateNca = true;

                        if (isUpdateNca)
                        {
                            openFileDialog1.Title = "Select base Nca";
                            openFileDialog1.ShowDialog();
                            var Input2 = File.OpenRead(openFileDialog1.FileName);
                            Patch = new Nca(Keys, Input2, true);
                            Nca.SetBaseNca(Patch);
                        }

                        new Thread
                        (
                            () =>
                            {
                                Thread.CurrentThread.IsBackground = true;
                                var Info = GetTitleMeta($"{Nca.Header.TitleId:x16}");

                                label2.Invoke(new Action(() => { label2.Text = Info[0]; label3.Text = Info[1]; }));
                            }
                        )
                        .Start();

                        Rom = new Romfs
                        (
                            Nca.OpenSection
                            (
                                Nca.Sections.FirstOrDefault
                                (s => s?.Type == SectionType.Romfs || s?.Type == SectionType.Bktr)
                                .SectionNum,
                                false,
                                IntegrityCheckLevel.None
                            )
                        );

                        IO.PopulateTreeView(treeView1.Nodes, Rom.RootDir);
                    }
                }
                catch
                {
                    MessageBox.Show("There was an error reading the NCA. Are you sure the correct keys are present in your keyfiles?");
                }
            }
            catch (ArgumentNullException)
            {
                MessageBox.Show("Error: key files are missing!");
            }
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.ShowDialog();
            Open();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (Program.FileArg != null)
            {
                Open();
            }
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.Node.Nodes.Count == 0)
                {
                    var Size = (decimal)Rom.FileDict[$"/{e.Node.FullPath}"].DataLength;

                    if (Size == 0)
                    {
                        label1.Text = $"Size: 0 bytes (empty file)";
                    }
                    else if (Size > 1024 && Size < 1048576)
                    {
                        Size /= 1024;
                        label1.Text = $"Size: {Size:0.00}KB";
                    }
                    else if (Size > 1048576 && Size < 1073741824)
                    {
                        Size /= 1048576;
                        label1.Text = $"Size: {Size:0.00}MB";
                    }
                    else if (Size > 1073741824)
                    {
                        Size /= 1073741824;
                        label1.Text = $"Size: {Size:0.00}GB";
                    }
                    else
                    {
                        label1.Text = $"Size: {Size} bytes";
                    }

                    var Name = Rom.FileDict[$"/{e.Node.FullPath}"].Name;

                    label4.Text = Name;

                    var Ext = Name.Split('.')[Name.Count(c => c == '.')].ToUpper();

                    if (CheckExtension(Ext, "bfstm"))
                    {
                        try
                        {
                            label6.Visible = true;
                            label7.Visible = true;

                            button2.Visible = true;
                            button3.Visible = true;

                            SoundFile = new VGAudio.Containers.NintendoWare.BCFstmReader().Read
                            (
                                Rom.OpenFile(Rom.FileDict[$"/{e.Node.FullPath}"])
                            )
                            .GetAllFormats()
                            .ToArray()[0];

                            label6.Text = $"Length: {new TimeSpan((SoundFile.SampleCount / SoundFile.SampleRate) * 10000000).ToString("mm':'ss")}";
                            label7.Text = $"Sample rate: {(decimal)(SoundFile.SampleRate / 1000)}KHz";
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else if (CheckExtension(Ext, "wav"))
                    {
                        try
                        {
                            label6.Visible = true;
                            label7.Visible = true;

                            button2.Visible = true;
                            button3.Visible = true;

                            SoundFile = new VGAudio.Containers.Wave.WaveReader().Read
                            (
                                Rom.OpenFile(Rom.FileDict[$"/{e.Node.FullPath}"])
                            )
                            .GetAllFormats()
                            .ToArray()[0];

                            label6.Text = $"Length: {new TimeSpan((SoundFile.SampleCount / SoundFile.SampleRate) * 10000000).ToString("mm':'ss")}";
                            label7.Text = $"Sample rate: {(decimal)(SoundFile.SampleRate / 1000)}KHz";
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else
                    {
                        label6.Visible = false;
                        label7.Visible = false;

                        button2.Visible = false;
                        button3.Visible = false;
                    }

                    label5.Text = $"{Ext} file";
                }
                else
                {
                    if (e.Node.Nodes.Count == 1)
                    {
                        label5.Text = "1 file";
                    }
                    else
                    {
                        label5.Text = $"{e.Node.Nodes.Count} files";
                    }

                    label4.Text = e.Node.Name;
                    label1.Text = "Folder";
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (treeView1.SelectedNode.Nodes.Count == 0)
                {
                    extractFileToolStripMenuItem.Text = "Extract file...";
                    contextMenuStrip1.Show(Cursor.Position);
                }
                else
                {
                    extractFileToolStripMenuItem.Text = "Extract folder...";
                    contextMenuStrip1.Show(Cursor.Position);
                }
            }
        }

        private void Extract()
        {
            string Dir = null;
            var Dialog = folderBrowserDialog1.ShowDialog();

            if (Dialog != DialogResult.Cancel)
            {
                Dir = folderBrowserDialog1.SelectedPath;
                if (treeView1.SelectedNode.Nodes.Count == 0)
                {
                    try
                    {
                        var File = Rom.FileDict[$"/{treeView1.SelectedNode.FullPath}"];
                        Rom.OpenFile(File).WriteAllBytes($"{Dir}/{File.Name}");
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    try
                    {
                        foreach (TreeNode node in treeView1.SelectedNode.Nodes)
                        {
                            var File = Rom.FileDict[$"/{node.FullPath}"];
                            Rom.OpenFile(File).WriteAllBytes($"{Dir}/{File.Name}");
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private void selectedFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Extract();
        }

        private void extractFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Extract();
        }

        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Nodes.Count == 0)
            {
                Extract();
            }
        }

        private void allFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            Rom.Extract(folderBrowserDialog1.SelectedPath);
        }

        private void expandAllNodesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1.ExpandAll();
        }

        private void menuStrip1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void treeView1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void treeView1_DragDrop_1(object sender, DragEventArgs e)
        {
            string[] File = (string[])e.Data.GetData(DataFormats.FileDrop);
            Program.FileArg = File[0];
            Open();
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            try
            {
                var Wav = new MemoryStream();

                new VGAudio.Containers.Wave.WaveWriter()
                {
                    Configuration = new VGAudio.Containers.Wave.WaveConfiguration
                    {
                        Codec = VGAudio.Containers.Wave.WaveCodec.Pcm16Bit
                    }
                }.WriteToStream(SoundFile.ToPcm16(), Wav);

                Wav.Position = 0;
                Audio = new SoundPlayer(Wav);
                Audio.PlayLooping();
            }
            catch (Exception)
            {
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                Audio.Stop();
            }
            catch (Exception)
            {
            }
        }

        private void dToolStripMenuItem_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            bool FoundExefs = false;

            for (int i = 0; i < 4; i++)
            {
                if (Nca.Header.ContentType == ContentType.Program && i == (int)ProgramPartitionType.Code)
                {
                    FoundExefs = true;
                    Nca.ExtractSection(i, folderBrowserDialog1.SelectedPath);
                }
            }

            if (!FoundExefs) MessageBox.Show("Error: this NCA does not contain an ExeFS partition.");
        }

        private void listOfFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            File.WriteAllLines($"{folderBrowserDialog1.SelectedPath}/{Nca.Header.TitleId:x16}_files.txt", Rom.FileDict.Keys);
        }

        private void iconToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Control != null)
            {
                using (var Rom = Control.OpenSection(0, false, IntegrityCheckLevel.None))
                {
                    var Romfs = new Romfs(Rom);
                    folderBrowserDialog1.ShowDialog();

                    using (var Icon = Romfs.OpenFile(Romfs.Files.FirstOrDefault(f => f.Name.Contains("icon"))))
                        Icon.WriteAllBytes($"{folderBrowserDialog1.SelectedPath}/{Control.Header.TitleId:x16}_icon.jpg");
                }
            }
            else if (pictureBox1.Image != null)
            {
                folderBrowserDialog1.ShowDialog();
                pictureBox1.Image.Save($"{folderBrowserDialog1.SelectedPath}/{Nca.Header.TitleId:x16}_icon.jpg");
            }
            else MessageBox.Show("Error: No control is present and icon is not in the database!");
        }

        private void rawToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Control != null)
            {
                using (var Rom = Control.OpenSection(0, false, IntegrityCheckLevel.None))
                {
                    var Romfs = new Romfs(Rom);
                    folderBrowserDialog1.ShowDialog();
                    using (var Nacp = Romfs.OpenFile(Romfs.Files.FirstOrDefault(f => f.Name == "control.nacp")))
                        Nacp.WriteAllBytes($"{folderBrowserDialog1.SelectedPath}/{Nca.Header.TitleId:x16}_control.nacp");
                }
            }
            else MessageBox.Show("Error: No control is present!");
        }

        private void jSONToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Control != null)
            {
                using (var Rom = Control.OpenSection(0, false, IntegrityCheckLevel.None))
                {
                    var Romfs = new Romfs(Rom);
                    folderBrowserDialog1.ShowDialog();
                    using (var InFile = Romfs.OpenFile(Romfs.Files.FirstOrDefault(f => f.Name == "control.nacp")))
                    using (var Read = new BinaryReader(InFile))
                    {
                        var Nacp = new Nacp(Read);

                        var Settings = new JsonSerializerSettings { Formatting = Formatting.Indented };

                        File.WriteAllText($"{folderBrowserDialog1.SelectedPath}/{Nca.Header.TitleId:x16}_control.json",
                            JsonConvert.SerializeObject(Nacp, Settings));
                    }
                }
            }
            else MessageBox.Show("Error: No control is present!");
        }
    }
}