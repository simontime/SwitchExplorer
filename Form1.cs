using LibHac;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Text;
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
        public Romfs Rom { get; set; }
        public static SoundPlayer Audio { get; set; }
        public IAudioFormat SoundFile { get; set; }

        public Form1()
        {
            InitializeComponent();
        }

        private string[] GetTitleMeta(string TitleID)
        {
            var Info = new string[]
            {
                "Title missing from database",
                ""
            };

            try
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
            catch (Exception)
            {
            }
            return Info;
        }

        private void Open()
        {
            treeView1.Nodes.Clear();

            string FileToOpen = null;

            if (Program.FileArg != null)
            {
                FileToOpen = Program.FileArg;
            }
            else
            {
                FileToOpen = openFileDialog1.FileName;
            }

            Program.FileArg = null;

            Stream Input = null;

            try
            {
                string ExpEnv(string In)
                {
                    return Environment.ExpandEnvironmentVariables(In);
                }

                var ProdKeys = ExpEnv(@"%USERPROFILE%\.switch\prod.keys");
                var TitleKeys = ExpEnv(@"%USERPROFILE%\.switch\title.keys");

                var Keys = ExternalKeys.ReadKeyFile(ProdKeys, TitleKeys);

                var Ext = (new FileInfo(FileToOpen).Extension);

                if (Ext == ".nsp")
                {
                    var InputPFS = File.OpenRead(FileToOpen);
                    var Pfs = new Pfs(InputPFS);

                    Input = Pfs.OpenFile
                    (
                        Pfs.Files.OrderByDescending(s => s.Size)
                        .FirstOrDefault()
                    );
                }
                else if (Ext == ".xci")
                {
                    var InputPFS = File.OpenRead(FileToOpen);
                    var Xci = new Xci(Keys, InputPFS);

                    Input = Xci.SecurePartition.OpenFile
                    (
                        Xci.SecurePartition.Files.OrderByDescending(s => s.Size)
                        .FirstOrDefault()
                    );
                }
                else
                {
                    Input = File.OpenRead(FileToOpen);
                }

                try
                {
                    Nca = new Nca(Keys, Input, true);

                    if (Nca.HasRightsId)
                    {
                        if (!Keys.TitleKeys.Keys.Contains(Nca.Header.RightsId))
                        {
                            MessageBox.Show($"Error: the titlekey for {Nca.Header.RightsId.ToHexString()} is not present in your key file.");
                        }
                    }

                    bool IsUpdateNca = false;

                    foreach (var Section in Nca.Sections)
                    {
                        if (Section?.Type == SectionType.Bktr)
                        {
                            IsUpdateNca = true;
                        }
                    }

                    if (IsUpdateNca)
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

                            label2.Invoke
                            (
                                new Action
                                (
                                    () => { label2.Text = Info[0]; }
                                )
                            );

                            label3.Invoke
                            (
                                new Action
                                (
                                    () => { label3.Text = Info[1]; }
                                )
                            );
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
                            false
                        )
                    );

                    var Files = new string[Rom.Files.Count];

                    for (int i = 0; i < Rom.Files.Count; i++)
                    {
                        Files[i] = Rom.Files[i].FullPath.Substring(1);
                    }

                    IO.PopulateTreeView(treeView1, Files, '/');
                }
                catch (Exception)
                {
                    MessageBox.Show("There was an error reading the NCA. Are you sure the correct keys are present in your keyfiles?");
                }
            }
            catch (Exception)
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

                    if (Ext == "BFSTM")
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
                    else if (Ext == "WAV")
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
            try
            {
                folderBrowserDialog1.ShowDialog();
                bool FoundExefs = false;
                foreach (var Section in Nca.Sections)
                {
                    if (Section.IsExefs)
                    {
                        FoundExefs = true;
                        Nca.ExtractSection(Section.SectionNum, folderBrowserDialog1.SelectedPath);
                    }
                }
                if (!FoundExefs)
                {
                    MessageBox.Show("Error: this NCA does not contain an ExeFS partition.");
                }
            }
            catch (Exception)
            {
            }
        }
    }
}