using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApp2
{
    /// <summary>
    /// Window3.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window3 : Window
    {
        private const string PositionFile = "window3_position.json";

        public Window3()
        {
            InitializeComponent();
            RestorePosition();
        }

        // 명확한 네임스페이스 지정
        protected override void OnActivated(EventArgs e)
        {

        }

        private void Grid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 현재 위치 저장
            SavePosition();
            System.Windows.MessageBox.Show($"현재 위치가 저장되었습니다.\nX: {this.Left}, Y: {this.Top}", "위치 저장");
            e.Handled = true; // 이벤트 전파 방지
        }

        private void SavePosition()
        {
            try
            {
                var position = new Window3Position
                {
                    Left = this.Left,
                    Top = this.Top
                };

                var json = JsonSerializer.Serialize(position);
                File.WriteAllText(PositionFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Window3 position save error: " + ex);
            }
        }

        private void RestorePosition()
        {
            try
            {
                if (!File.Exists(PositionFile)) return;

                var json = File.ReadAllText(PositionFile);
                var position = JsonSerializer.Deserialize<Window3Position>(json);

                if (position != null)
                {
                    this.Left = position.Left;
                    this.Top = position.Top;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Window3 position restore error: " + ex);
            }
        }
    }

    public class Window3Position
    {
        public double Left { get; set; }
        public double Top { get; set; }
    }
}
