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
using AForge;
using AForge.Imaging;
using AForge.Controls;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AForge.Imaging.Filters;
using SVM;

namespace Hand_Gesture_Recognition
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private FilterInfoCollection CaptureDevice;

        private VideoCaptureDevice FinalFrame;

        int x = 0, y = 0, label = 0,camera = 0;

        private void Form1_Load(object sender, EventArgs e)
        {
            Disconnect.Enabled = false;
            CaptureDevice = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach(FilterInfo Device in CaptureDevice)
            {
                comboBox1.Items.Add(Device.Name);
            }
            comboBox1.SelectedIndex = 0;
            FinalFrame = new VideoCaptureDevice();
        }

        private void Connect_Click(object sender, EventArgs e)
        {
            camera = 1;
            Connect.Enabled = false;
            Disconnect.Enabled = true;
            FinalFrame = new VideoCaptureDevice(CaptureDevice[comboBox1.SelectedIndex].MonikerString);
            FinalFrame.NewFrame += new NewFrameEventHandler(FinalFrame_NewFrame);
            FinalFrame.Start();
        }

        void FinalFrame_NewFrame(object sender,NewFrameEventArgs eventArgs)
        {
            pictureBox1.Image = (Bitmap)eventArgs.Frame.Clone();
        }

        private void Disconnect_Click(object sender, EventArgs e)
        {
            camera = 0;
            Connect.Enabled = true;
            Disconnect.Enabled = false;
            FinalFrame.Stop();
            pictureBox1.Image = null;
            pictureBox2.Image = null;
            label1.Text = null;
            textBox1.Text = null;
            textBox1.Enabled = false;
            progressBar1.Value = 0;
            
        }

        private void Detect_Click(object sender, EventArgs e)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if(pictureBox1.Image != null)
            { 

            progressBar1.Visible = true;
            progressBar1.Minimum = 0;
            progressBar1.Maximum = 8;
            progressBar1.Value = 1;
            progressBar1.Step = 1;

                pictureBox2.Image = (Bitmap)pictureBox1.Image.Clone();
                Bitmap src = new Bitmap(pictureBox2.Image);

                //skin detection
                var image = new Rectangle(0, 0, src.Width, src.Height);
                var value = src.LockBits(image, ImageLockMode.ReadWrite, src.PixelFormat);
                var size = Bitmap.GetPixelFormatSize(value.PixelFormat) / 8;
                var buffer = new byte[value.Width * value.Height * size];
                Marshal.Copy(value.Scan0, buffer, 0, buffer.Length);

                System.Threading.Tasks.Parallel.Invoke(
                    () =>
                    {
                        Process(buffer, 0, 0, value.Width / 2, value.Height / 2, value.Width, size);
                    },
                    () =>
                    {
                        Process(buffer, 0, value.Height / 2, value.Width / 2, value.Height, value.Width, size);
                    },
                    () =>
                    {
                        Process(buffer, value.Width / 2, 0, value.Width, value.Height / 2, value.Width, size);
                    },
                    () =>
                    {
                        Process(buffer, value.Width / 2, value.Height / 2, value.Width, value.Height, value.Width, size);
                    }
                );
                Marshal.Copy(buffer, 0, value.Scan0, buffer.Length);
                src.UnlockBits(value);
                progressBar1.PerformStep();

                //Dilation & Erosion
                GrayscaleBT709 gray = new GrayscaleBT709();
                src = gray.Apply(src);
                Dilatation dilatation = new Dilatation();
                Erosion erosion = new Erosion();
                src = dilatation.Apply(src);
                src = erosion.Apply(src);
                progressBar1.PerformStep();


                //Blob
                try
                {
                    ExtractBiggestBlob blob = new ExtractBiggestBlob();
                    src = blob.Apply(src);
                    x = blob.BlobPosition.X;
                    y = blob.BlobPosition.Y;
                    progressBar1.PerformStep();
                }
                catch
                {
                    MessageBox.Show("Lightning conditions are not good for detecting the gestures","Bad Lights",MessageBoxButtons.OK,MessageBoxIcon.Information);
                }

                //Merge
                Bitmap srcImage = new Bitmap(pictureBox2.Image);
                Bitmap dstImage = new Bitmap(src);
                var srcrect = new Rectangle(0, 0, srcImage.Width, srcImage.Height);
                var dstrect = new Rectangle(0, 0, dstImage.Width, dstImage.Height);
                var srcdata = srcImage.LockBits(srcrect, ImageLockMode.ReadWrite, srcImage.PixelFormat);
                var dstdata = dstImage.LockBits(dstrect, ImageLockMode.ReadWrite, dstImage.PixelFormat);
                var srcdepth = Bitmap.GetPixelFormatSize(srcdata.PixelFormat) / 8;
                var dstdepth = Bitmap.GetPixelFormatSize(dstdata.PixelFormat) / 8;
                //bytes per pixel
                var srcbuffer = new byte[srcdata.Width * srcdata.Height * srcdepth];
                var dstbuffer = new byte[dstdata.Width * dstdata.Height * dstdepth];
                //copy pixels to buffer
                Marshal.Copy(srcdata.Scan0, srcbuffer, 0, srcbuffer.Length);
                Marshal.Copy(dstdata.Scan0, dstbuffer, 0, dstbuffer.Length);
              
                    System.Threading.Tasks.Parallel.Invoke(
                        () =>
                        {
                        //upper-left
                        Process1(srcbuffer, dstbuffer, x, 0, y, 0, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height / 2), dstdata.Height / 2, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                        //upper-right
                        Process1(srcbuffer, dstbuffer, x + (dstdata.Width / 2), dstdata.Width / 2, y, 0, x + (dstdata.Width), dstdata.Width, y + (dstdata.Height / 2), dstdata.Height / 2, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                        //lower-left
                        Process1(srcbuffer, dstbuffer, x, 0, y + (dstdata.Height / 2), dstdata.Height / 2, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height), dstdata.Height, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                        //lower-right
                        Process1(srcbuffer, dstbuffer, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height / 2), dstdata.Height / 2, x + (dstdata.Width), dstdata.Width, y + (dstdata.Height), dstdata.Height, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        }
                    );
               
                //Copy the buffer back to image
                Marshal.Copy(srcbuffer, 0, srcdata.Scan0, srcbuffer.Length);
                Marshal.Copy(dstbuffer, 0, dstdata.Scan0, dstbuffer.Length);
                srcImage.UnlockBits(srcdata);
                dstImage.UnlockBits(dstdata);
                src = dstImage;
                progressBar1.PerformStep();


                //Resize
                ResizeBilinear resize = new ResizeBilinear(200, 200);
                src = resize.Apply(src);
                progressBar1.PerformStep();


                //Edges
                src = gray.Apply((Bitmap)src);
                CannyEdgeDetector edges = new CannyEdgeDetector();
                src = edges.Apply(src);
                progressBar1.PerformStep();


                //HOEF
                Bitmap block = new Bitmap(src);
                int[] edgescount = new int[50];
                double[] norm = new double[200];
                String text = null;
                int sum = 0;
                int z = 1;
                for (int p = 1; p <= 6; p++)
                {
                    for (int q = 1; q <= 6; q++)
                    {
                        for (int x = (p - 1) * block.Width / 6; x < (p * block.Width / 6); x++)
                        {
                            for (int y = (q - 1) * block.Height / 6; y < (q * block.Height / 6); y++)
                            {
                                Color colorPixel = block.GetPixel(x, y);

                                int r = colorPixel.R;
                                int g = colorPixel.G;
                                int b = colorPixel.B;

                                if (r != 0 & g != 0 & b != 0)
                                    edgescount[z]++;
                            }

                        }
                        z++;
                    }
                }

                for (z = 1; z <= 36; z++) sum = sum + edgescount[z];
                for (z = 1; z <= 36; z++)
                {
                    norm[z] = (double)edgescount[z] / sum;
                    text = text + " " + z.ToString() + ":" + norm[z].ToString();
                }

                File.WriteAllText(@"d:\test.txt", label.ToString() + text + Environment.NewLine);
                progressBar1.PerformStep();


                //SVM
                Problem train = Problem.Read(@"D:\train.txt");
                Problem test = Problem.Read(@"D:\test.txt");
                Parameter parameter = new Parameter();
                parameter.C = 32;
                parameter.Gamma = 8;
                Model model = Training.Train(train, parameter);
                Prediction.Predict(test, @"D:\result.txt", model, false);
                int value1 = Convert.ToInt32(File.ReadAllText(@"D:\result.txt"));
                String alphabet = null;
                alphabet += (char)(65 + value1);
                label1.Text = alphabet;
                progressBar1.PerformStep();
            }
            else
                this.Show();
            watch.Stop();
            var time = (watch.ElapsedMilliseconds);
            float secs = (float)time/1000;
            textBox1.Text = Convert.ToString(secs) +" "+ "Seconds";
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            if (camera == 0)
            {
                OpenFileDialog open = new OpenFileDialog();
                if (open.ShowDialog() == DialogResult.OK)
                {
                    Bitmap image = new Bitmap(open.FileName);
                    pictureBox1.Image = (Bitmap)image;
                    pictureBox2.Image = null;
                }
            }
            else
            {
                MessageBox.Show("Disconnect the camera","Warning",MessageBoxButtons.OK,MessageBoxIcon.Exclamation);
            }
        }

        public void Process(byte[] buffer, int x, int y, int X, int Y, int length, int size)
        {
            for (int i = x; i < X; i++)
            {
                for (int j = y; j < Y; j++)
                {
                    var displacement = ((j * length) + i) * size;
                    var r = buffer[displacement + 2];
                    var g = buffer[displacement + 1];
                    var b = buffer[displacement + 0];
                    if (r >= 45 & r <= 255 & g > 34 & g <= 229 & b >= 15 & b <= 200 & r - g >= 11 & r - b >= 15 & g - b >= 4 & r > g & r > b & g > b)
                        buffer[displacement + 0] = buffer[displacement + 1] = buffer[displacement + 2] = 255;
                    else
                        buffer[displacement + 0] = buffer[displacement + 1] = buffer[displacement + 2] = 0;
                }
            }

        }

        public void Process1(byte[] srbuffer, byte[] dsbuffer, int srcx, int dstx, int srcy, int dsty, int srcendx, int dstendx, int srcendy, int dstendy, int srcwidth, int dstwidth, int srdepth, int dsdepth)
        {
            
                for (int i = srcx, m = dstx; (i < srcendx & m < dstendx); i++, m++)
                {
                    for (int j = srcy, n = dsty; (j < srcendy & n < dstendy); j++, n++)
                    {
                        var offset = ((j * srcwidth) + i) * srdepth;
                        var offset1 = ((n * dstwidth) + m) * dsdepth;

                        var srcB = srbuffer[offset + 0];
                        var srcG = srbuffer[offset + 1];
                        var srcR = srbuffer[offset + 2];
                        var dstB = dsbuffer[offset1 + 0];
                        var dstG = dsbuffer[offset1 + 1];
                        var dstR = dsbuffer[offset1 + 2];
                        if (dstR != 0 & dstG != 0 & dstB != 0)
                        {
                            dsbuffer[offset1 + 0] = srbuffer[offset + 0];
                            dsbuffer[offset1 + 1] = srbuffer[offset + 1];
                            dsbuffer[offset1 + 2] = srbuffer[offset + 2];
                        }
                    }
                }
            
           
        }
    }
}
