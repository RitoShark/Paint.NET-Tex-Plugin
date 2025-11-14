using PaintDotNet;
using System;
using System.Windows.Forms;

namespace TexFileTypePlugin
{
    public class TexSaveConfigWidget : SaveConfigWidget
    {
        private RadioButton radioDxt1;
        private RadioButton radioDxt5;
        private RadioButton radioRgba8;
        private GroupBox groupBox;
        private Button refreshButton;
        private bool isUpdating = false;

        public TexSaveConfigWidget()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Group box
            groupBox = new GroupBox
            {
                Text = "Compression Format",
                Location = new System.Drawing.Point(5, 5),
                Size = new System.Drawing.Size(180, 115),
                ForeColor = System.Drawing.Color.White
            };

            // DXT1 / BC1 radio button
            radioDxt1 = new RadioButton
            {
                Text = "DXT1 / BC1",
                Location = new System.Drawing.Point(10, 25),
                Size = new System.Drawing.Size(290, 20),
                AutoSize = false,
                ForeColor = System.Drawing.Color.White
            };
            radioDxt1.CheckedChanged += OnFormatChanged;

            // DXT5 / BC3 radio button
            radioDxt5 = new RadioButton
            {
                Text = "DXT5 / BC3",
                Location = new System.Drawing.Point(10, 50),
                Size = new System.Drawing.Size(290, 20),
                AutoSize = false,
                Checked = true,
                ForeColor = System.Drawing.Color.White
            };
            radioDxt5.CheckedChanged += OnFormatChanged;

            // BGRA8 radio button
            radioRgba8 = new RadioButton
            {
                Text = "BGRA8 Uncompressed",
                Location = new System.Drawing.Point(10, 75),
                Size = new System.Drawing.Size(290, 20),
                AutoSize = false,
                ForeColor = System.Drawing.Color.White
            };
            radioRgba8.CheckedChanged += OnFormatChanged;

            groupBox.Controls.Add(radioDxt1);
            groupBox.Controls.Add(radioDxt5);
            groupBox.Controls.Add(radioRgba8);

            // Refresh button - simple default button with white background and black text
            refreshButton = new Button
            {
                Text = "Refresh Preview",
                Location = new System.Drawing.Point(35, 128),
                Size = new System.Drawing.Size(130, 30),
                BackColor = System.Drawing.Color.White,
                ForeColor = System.Drawing.Color.Black,
                UseVisualStyleBackColor = false
            };
            refreshButton.Click += OnRefreshClick;

            this.Controls.Add(groupBox);
            this.Controls.Add(refreshButton);
            this.Size = new System.Drawing.Size(320, 168);
            this.ResumeLayout(false);
        }

        private void OnFormatChanged(object? sender, EventArgs e)
        {
            if (isUpdating) return;
            
            if (Token is TexSaveConfigToken token)
            {
                if (radioDxt1.Checked)
                {
                    token.Compression = CompressionType.DXT1_BC1;
                }
                else if (radioDxt5.Checked)
                {
                    token.Compression = CompressionType.DXT5_BC3;
                }
                else if (radioRgba8.Checked)
                {
                    token.Compression = CompressionType.RGBA8_Uncompressed;
                }
            }
        }

        private void OnRefreshClick(object? sender, EventArgs e)
        {
            // Update the token first
            OnFormatChanged(null, EventArgs.Empty);
            
            // Notify Paint.NET to refresh the preview and file size
            UpdateToken();
        }

        protected override void InitWidgetFromToken(SaveConfigToken sourceToken)
        {
            isUpdating = true;
            try
            {
                if (sourceToken is TexSaveConfigToken token)
                {
                    switch (token.Compression)
                    {
                        case CompressionType.DXT1_BC1:
                            radioDxt1.Checked = true;
                            break;
                        case CompressionType.DXT5_BC3:
                            radioDxt5.Checked = true;
                            break;
                        case CompressionType.RGBA8_Uncompressed:
                            radioRgba8.Checked = true;
                            break;
                        default:
                            radioDxt5.Checked = true;
                            break;
                    }
                }
            }
            finally
            {
                isUpdating = false;
            }
        }

        protected override void InitTokenFromWidget()
        {
            OnFormatChanged(null, EventArgs.Empty);
        }
    }
}
