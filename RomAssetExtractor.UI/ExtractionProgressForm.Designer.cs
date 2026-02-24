
namespace RomAssetExtractor.Ui
{
    partial class ExtractionProgressForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnRomPath = new System.Windows.Forms.Button();
            this.btnOutputPath = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.txtRomPath = new System.Windows.Forms.TextBox();
            this.txtOutputPath = new System.Windows.Forms.TextBox();
            this.btnExtract = new System.Windows.Forms.Button();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.chkBitmaps = new System.Windows.Forms.CheckBox();
            this.chkTrainers = new System.Windows.Forms.CheckBox();
            this.chkMaps = new System.Windows.Forms.CheckBox();
            this.chkMapRenders = new System.Windows.Forms.CheckBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtDecompPath = new System.Windows.Forms.TextBox();
            this.btnDecompPath = new System.Windows.Forms.Button();
            this.chkGodotMaps = new System.Windows.Forms.CheckBox();
            this.chkDecompToGodot = new System.Windows.Forms.CheckBox();
            this.chkSaveSpawns = new System.Windows.Forms.CheckBox();
            this.grpRomOptions = new System.Windows.Forms.GroupBox();
            this.grpDecompOptions = new System.Windows.Forms.GroupBox();
            this.grpRomOptions.SuspendLayout();
            this.grpDecompOptions.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnRomPath
            // 
            this.btnRomPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRomPath.Location = new System.Drawing.Point(489, 10);
            this.btnRomPath.Name = "btnRomPath";
            this.btnRomPath.Size = new System.Drawing.Size(64, 20);
            this.btnRomPath.TabIndex = 0;
            this.btnRomPath.Text = "Browse...";
            this.btnRomPath.UseVisualStyleBackColor = true;
            this.btnRomPath.Click += new System.EventHandler(this.btnRomPath_Click);
            // 
            // btnOutputPath
            // 
            this.btnOutputPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOutputPath.Location = new System.Drawing.Point(489, 35);
            this.btnOutputPath.Name = "btnOutputPath";
            this.btnOutputPath.Size = new System.Drawing.Size(64, 20);
            this.btnOutputPath.TabIndex = 1;
            this.btnOutputPath.Text = "Browse...";
            this.btnOutputPath.UseVisualStyleBackColor = true;
            this.btnOutputPath.Click += new System.EventHandler(this.btnOutputPath_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(10, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(120, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Path to Pokémon ROM:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(10, 38);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(148, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Path to asset output directory:";
            // 
            // txtRomPath
            // 
            this.txtRomPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtRomPath.Location = new System.Drawing.Point(158, 10);
            this.txtRomPath.Name = "txtRomPath";
            this.txtRomPath.Size = new System.Drawing.Size(326, 20);
            this.txtRomPath.TabIndex = 4;
            // 
            // txtOutputPath
            // 
            this.txtOutputPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtOutputPath.Location = new System.Drawing.Point(158, 35);
            this.txtOutputPath.Name = "txtOutputPath";
            this.txtOutputPath.Size = new System.Drawing.Size(326, 20);
            this.txtOutputPath.TabIndex = 5;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(10, 63);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(131, 13);
            this.label3.TabIndex = 12;
            this.label3.Text = "Path to decomp project:";
            // 
            // txtDecompPath
            // 
            this.txtDecompPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtDecompPath.Location = new System.Drawing.Point(158, 60);
            this.txtDecompPath.Name = "txtDecompPath";
            this.txtDecompPath.Size = new System.Drawing.Size(326, 20);
            this.txtDecompPath.TabIndex = 13;
            // 
            // btnDecompPath
            // 
            this.btnDecompPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDecompPath.Location = new System.Drawing.Point(489, 60);
            this.btnDecompPath.Name = "btnDecompPath";
            this.btnDecompPath.Size = new System.Drawing.Size(64, 20);
            this.btnDecompPath.TabIndex = 14;
            this.btnDecompPath.Text = "Browse...";
            this.btnDecompPath.UseVisualStyleBackColor = true;
            this.btnDecompPath.Click += new System.EventHandler(this.btnDecompPath_Click);
            // 
            // grpRomOptions
            // 
            this.grpRomOptions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpRomOptions.Controls.Add(this.chkBitmaps);
            this.grpRomOptions.Controls.Add(this.chkTrainers);
            this.grpRomOptions.Controls.Add(this.chkMaps);
            this.grpRomOptions.Controls.Add(this.chkMapRenders);
            this.grpRomOptions.Controls.Add(this.chkGodotMaps);
            this.grpRomOptions.Location = new System.Drawing.Point(10, 90);
            this.grpRomOptions.Name = "grpRomOptions";
            this.grpRomOptions.Size = new System.Drawing.Size(268, 90);
            this.grpRomOptions.TabIndex = 15;
            this.grpRomOptions.TabStop = false;
            this.grpRomOptions.Text = "ROM Extraction";
            // 
            // chkBitmaps
            // 
            this.chkBitmaps.AutoSize = true;
            this.chkBitmaps.Location = new System.Drawing.Point(10, 18);
            this.chkBitmaps.Name = "chkBitmaps";
            this.chkBitmaps.Size = new System.Drawing.Size(137, 17);
            this.chkBitmaps.TabIndex = 8;
            this.chkBitmaps.Text = "Save all sprites as PNG";
            this.chkBitmaps.UseVisualStyleBackColor = true;
            // 
            // chkTrainers
            // 
            this.chkTrainers.AutoSize = true;
            this.chkTrainers.Checked = true;
            this.chkTrainers.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkTrainers.Location = new System.Drawing.Point(10, 38);
            this.chkTrainers.Name = "chkTrainers";
            this.chkTrainers.Size = new System.Drawing.Size(118, 17);
            this.chkTrainers.TabIndex = 9;
            this.chkTrainers.Text = "Also extract trainers";
            this.chkTrainers.UseVisualStyleBackColor = true;
            // 
            // chkMaps
            // 
            this.chkMaps.AutoSize = true;
            this.chkMaps.Checked = true;
            this.chkMaps.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkMaps.Location = new System.Drawing.Point(10, 58);
            this.chkMaps.Name = "chkMaps";
            this.chkMaps.Size = new System.Drawing.Size(109, 17);
            this.chkMaps.TabIndex = 10;
            this.chkMaps.Text = "Also extract maps";
            this.chkMaps.UseVisualStyleBackColor = true;
            this.chkMaps.CheckedChanged += new System.EventHandler(this.chkMaps_CheckedChanged);
            // 
            // chkMapRenders
            // 
            this.chkMapRenders.AutoSize = true;
            this.chkMapRenders.Checked = true;
            this.chkMapRenders.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkMapRenders.Location = new System.Drawing.Point(140, 18);
            this.chkMapRenders.Name = "chkMapRenders";
            this.chkMapRenders.Size = new System.Drawing.Size(119, 17);
            this.chkMapRenders.TabIndex = 11;
            this.chkMapRenders.Text = "Create map renders";
            this.chkMapRenders.UseVisualStyleBackColor = true;
            // 
            // chkGodotMaps
            // 
            this.chkGodotMaps.AutoSize = true;
            this.chkGodotMaps.Location = new System.Drawing.Point(140, 38);
            this.chkGodotMaps.Name = "chkGodotMaps";
            this.chkGodotMaps.Size = new System.Drawing.Size(124, 17);
            this.chkGodotMaps.TabIndex = 16;
            this.chkGodotMaps.Text = "Export Godot maps";
            this.chkGodotMaps.UseVisualStyleBackColor = true;
            // 
            // grpDecompOptions
            // 
            this.grpDecompOptions.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.grpDecompOptions.Controls.Add(this.chkDecompToGodot);
            this.grpDecompOptions.Controls.Add(this.chkSaveSpawns);
            this.grpDecompOptions.Location = new System.Drawing.Point(284, 90);
            this.grpDecompOptions.Name = "grpDecompOptions";
            this.grpDecompOptions.Size = new System.Drawing.Size(268, 90);
            this.grpDecompOptions.TabIndex = 17;
            this.grpDecompOptions.TabStop = false;
            this.grpDecompOptions.Text = "Decomp → Godot";
            // 
            // chkDecompToGodot
            // 
            this.chkDecompToGodot.AutoSize = true;
            this.chkDecompToGodot.Location = new System.Drawing.Point(10, 18);
            this.chkDecompToGodot.Name = "chkDecompToGodot";
            this.chkDecompToGodot.Size = new System.Drawing.Size(176, 17);
            this.chkDecompToGodot.TabIndex = 18;
            this.chkDecompToGodot.Text = "Convert decomp maps to Godot";
            this.chkDecompToGodot.UseVisualStyleBackColor = true;
            this.chkDecompToGodot.CheckedChanged += new System.EventHandler(this.chkDecompToGodot_CheckedChanged);
            // 
            // chkSaveSpawns
            // 
            this.chkSaveSpawns.AutoSize = true;
            this.chkSaveSpawns.Location = new System.Drawing.Point(10, 38);
            this.chkSaveSpawns.Name = "chkSaveSpawns";
            this.chkSaveSpawns.Size = new System.Drawing.Size(168, 17);
            this.chkSaveSpawns.TabIndex = 19;
            this.chkSaveSpawns.Text = "Save spawn data as JSON";
            this.chkSaveSpawns.Enabled = false;
            this.chkSaveSpawns.UseVisualStyleBackColor = true;
            // 
            // btnExtract
            // 
            this.btnExtract.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnExtract.Location = new System.Drawing.Point(10, 186);
            this.btnExtract.Name = "btnExtract";
            this.btnExtract.Size = new System.Drawing.Size(543, 24);
            this.btnExtract.TabIndex = 6;
            this.btnExtract.Text = "Start Extraction";
            this.btnExtract.UseVisualStyleBackColor = true;
            this.btnExtract.Click += new System.EventHandler(this.btnExtract_Click);
            // 
            // txtOutput
            // 
            this.txtOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtOutput.Location = new System.Drawing.Point(10, 216);
            this.txtOutput.Multiline = true;
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtOutput.Size = new System.Drawing.Size(543, 224);
            this.txtOutput.TabIndex = 7;
            // 
            // ExtractionProgressForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(564, 451);
            this.Controls.Add(this.grpDecompOptions);
            this.Controls.Add(this.grpRomOptions);
            this.Controls.Add(this.txtOutput);
            this.Controls.Add(this.btnExtract);
            this.Controls.Add(this.txtOutputPath);
            this.Controls.Add(this.txtDecompPath);
            this.Controls.Add(this.txtRomPath);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnDecompPath);
            this.Controls.Add(this.btnOutputPath);
            this.Controls.Add(this.btnRomPath);
            this.Name = "ExtractionProgressForm";
            this.Text = "Rom Asset Extractor";
            this.Load += new System.EventHandler(this.ExtractionProgressForm_Load);
            this.grpRomOptions.ResumeLayout(false);
            this.grpRomOptions.PerformLayout();
            this.grpDecompOptions.ResumeLayout(false);
            this.grpDecompOptions.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnRomPath;
        private System.Windows.Forms.Button btnOutputPath;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtRomPath;
        private System.Windows.Forms.TextBox txtOutputPath;
        private System.Windows.Forms.Button btnExtract;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.CheckBox chkBitmaps;
        private System.Windows.Forms.CheckBox chkTrainers;
        private System.Windows.Forms.CheckBox chkMaps;
        private System.Windows.Forms.CheckBox chkMapRenders;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtDecompPath;
        private System.Windows.Forms.Button btnDecompPath;
        private System.Windows.Forms.CheckBox chkGodotMaps;
        private System.Windows.Forms.CheckBox chkDecompToGodot;
        private System.Windows.Forms.CheckBox chkSaveSpawns;
        private System.Windows.Forms.GroupBox grpRomOptions;
        private System.Windows.Forms.GroupBox grpDecompOptions;
    }
}

