using System;
using System.Windows.Forms;
using System.Threading.Tasks;
using AccessProcessMemory;
namespace sample
{
    public partial class Form1 : Form
    {
        private bool isStarted;

        public Form1()
        {
            InitializeComponent();

            this.isStarted = false;
        }

        delegate void FormDelegate();

        private void button1_Click(object sender, EventArgs e)
        {
            if (this.isStarted)
            {
                this.isStarted = false;
                this.button1.Text = "start";
            }
            else
            {
                this.isStarted = true;
                this.button1.Text = "stop";

                var exeFile = this.exeFile.Text;
                var windowText = this.windowText.Text;
                var address = this.address.Text;
                Task.Factory.StartNew(()=> 
                {
                    using (var c = new ProcessControl(exeFile, windowText))
                    {
                        if (!c.isOpened)
                        {
                            this.Invoke(new FormDelegate(() =>
                            {
                                this.label1.Text = "Failed";
                                this.isStarted = false;
                                this.button1.Text = "start";
                            }));
                            return;
                        }
                        c.Activate();
                        uint data = 0;
                        uint data_old = 0;
                        while (this.isStarted)
                        {
                            var buffer = c.Read(new IntPtr(Convert.ToUInt32(address, 16)), sizeof(uint));
                            data_old = data;
                            data = BitConverter.ToUInt32(buffer, 0);
                            if (data_old != data)
                            {
                                this.Invoke(new FormDelegate(() => 
                                {
                                    this.label1.Text = data.ToString();
                                }));
                            }
                        }
                    }
                });
            }
        }
    }
}
