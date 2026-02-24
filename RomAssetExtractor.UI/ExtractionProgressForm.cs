using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RomAssetExtractor.Godot;
using DecompToGodot;

namespace RomAssetExtractor.Ui
{
    public partial class ExtractionProgressForm : Form
    {
        public ExtractionProgressForm()
        {
            InitializeComponent();
        }

        private void ExtractionProgressForm_Load(object sender, EventArgs e)
        {

        }

        private void btnRomPath_Click(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "GBA File (*.gba)|*.gba",
                Title = "Choose a Pokémon ROM-file to extract the assets from"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            txtRomPath.Text = dialog.FileName;
        }

        private void btnOutputPath_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Choose the output directory"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            txtOutputPath.Text = dialog.SelectedPath;
        }

        private void btnDecompPath_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Choose the decomp project root (e.g. pokefirered-master)"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            txtDecompPath.Text = dialog.SelectedPath;
        }

        private void chkMaps_CheckedChanged(object sender, EventArgs e)
        {
            chkMapRenders.Enabled = chkMapRenders.Checked = chkMaps.Checked;
            chkGodotMaps.Enabled = chkMaps.Checked;
            if (!chkMaps.Checked)
                chkGodotMaps.Checked = false;
        }

        private void chkDecompToGodot_CheckedChanged(object sender, EventArgs e)
        {
            chkSaveSpawns.Enabled = chkDecompToGodot.Checked;
            if (!chkDecompToGodot.Checked)
                chkSaveSpawns.Checked = false;
        }

        private async void btnExtract_Click(object sender, EventArgs e)
        {
            var romPath = txtRomPath.Text;
            var outputDirectory = txtOutputPath.Text;
            var decompPath = txtDecompPath.Text;

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                MessageBox.Show("No output path specified!", "Invalid path!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Directory.CreateDirectory(outputDirectory);

            if (!Directory.Exists(outputDirectory))
            {
                MessageBox.Show("Invalid output path specified!", "Invalid path!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool doRomExtract = !string.IsNullOrWhiteSpace(romPath);
            bool doDecomp = chkDecompToGodot.Checked && !string.IsNullOrWhiteSpace(decompPath);

            if (doRomExtract && !File.Exists(romPath))
            {
                MessageBox.Show("Invalid ROM path specified!", "Invalid path!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (doDecomp && !Directory.Exists(decompPath))
            {
                MessageBox.Show("Invalid decomp path specified!", "Invalid path!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!doRomExtract && !doDecomp)
            {
                MessageBox.Show("Please specify a ROM path or a decomp path (or both).", "Nothing to do!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Disable buttons during extraction
            var buttons = new Control[] { btnExtract, btnOutputPath, btnRomPath, btnDecompPath };
            foreach (var btn in buttons)
                btn.Enabled = false;

            var logWriter = new TextBoxWriter(txtOutput);
            txtOutput.Clear();

            try
            {
                // --- ROM extraction ---
                if (doRomExtract)
                {
                    Action<Pokemon.Entities.Bank[], string> mapPostProcessor = null;
                    if (chkGodotMaps.Checked)
                        mapPostProcessor = GodotMapExporter.ExportAllMaps;

                    await AssetExtractor.ExtractRom(
                        romPath,
                        outputDirectory,
                        chkBitmaps.Checked,
                        chkTrainers.Checked,
                        chkMaps.Checked,
                        chkMapRenders.Checked,
                        logWriter: logWriter,
                        mapPostProcessor: mapPostProcessor);
                }

                // --- Decomp to Godot conversion ---
                if (doDecomp)
                {
                    logWriter.WriteLine();
                    logWriter.WriteLine("=== Decomp → Godot Conversion ===");
                    logWriter.Flush();

                    await Task.Run(() =>
                    {
                        var converter = new Converter(decompPath, outputDirectory, null);
                        converter.SaveSpawns = chkSaveSpawns.Checked;
                        converter.Run();
                    });

                    logWriter.WriteLine("Decomp → Godot conversion complete.");
                    logWriter.Flush();
                }

                logWriter.WriteLine();
                logWriter.WriteLine("All done!");
                logWriter.Flush();
            }
            catch (Exception ex)
            {
                logWriter.WriteLine();
                logWriter.WriteLine($"ERROR: {ex.Message}");
                logWriter.WriteLine(ex.StackTrace);
                logWriter.Flush();
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                foreach (var btn in buttons)
                    btn.Enabled = true;
            }
        }
    }
}
