using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FBXtoRVT.UI
{
    /// <summary>
    /// 라디오로 고르는 "프리셋" 하나. (라디오 제목 + 그때 채워 넣을 Main / Sub 문자열)
    /// 아직 값을 정하지 않은 자리는 Enabled 를 false 로 두면 고를 수 없는 상태로 표시된다.
    /// </summary>
    public class MainSubPreset
    {
        public string Title { get; set; }   // 라디오 버튼에 보이는 제목
        public string Main { get; set; }    // 고르면 Main 칸에 들어갈 문자열
        public string Sub { get; set; }     // 고르면 Sub 칸에 들어갈 문자열
        public bool Enabled { get; set; }   // false 면 아직 정하지 않은 자리(선택 불가)

        public MainSubPreset(string title, string main, string sub, bool enabled = true)
        {
            Title = title;
            Main = main;
            Sub = sub;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// Main / Sub 문자열 2개를 입력받는 WPF 창.
    /// 위쪽 라디오(프리셋)를 고르면 Main / Sub 칸이 그 프리셋 값으로 채워지고,
    /// 그 상태에서 직접 고쳐서 쓸 수도 있다.
    /// </summary>
    public partial class MainSubWindow : Window
    {
        // 명령 쪽에서 읽어갈 결과 값
        public string MainText { get; private set; }
        public string SubText { get; private set; }

        // 프리셋을 눌렀을 때 칸을 채우기 위해 보관
        private readonly List<MainSubPreset> presets;

        /// <param name="title">창 제목</param>
        /// <param name="presets">위쪽에 표시할 프리셋 목록(라디오 버튼이 된다)</param>
        /// <param name="selectedIndex">처음에 골라 둘 프리셋 번호(0부터)</param>
        public MainSubWindow(string title, IList<MainSubPreset> presets, int selectedIndex)
        {
            InitializeComponent();

            Title = title;
            this.presets = new List<MainSubPreset>(presets ?? new List<MainSubPreset>());

            BuildPresetRadios(selectedIndex);
        }

        /// <summary>
        /// 프리셋 목록으로 라디오 버튼을 만들어 붙인다.
        /// </summary>
        private void BuildPresetRadios(int selectedIndex)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                MainSubPreset preset = presets[i];

                var radio = new RadioButton
                {
                    Content = preset.Title,
                    GroupName = "MainSubPreset",
                    IsEnabled = preset.Enabled,
                    Margin = new Thickness(0, 2, 14, 2),
                    Tag = i                       // 몇 번째 프리셋인지 기억
                };

                radio.Checked += Preset_Checked;
                PresetPanel.Children.Add(radio);

                // 처음에 골라 둘 프리셋 (선택 불가 자리는 건너뛴다)
                if (i == selectedIndex && preset.Enabled)
                    radio.IsChecked = true;
            }
        }

        /// <summary>
        /// 프리셋 라디오를 고르면 Main / Sub 칸을 그 값으로 바꾼다.
        /// </summary>
        private void Preset_Checked(object sender, RoutedEventArgs e)
        {
            var radio = sender as RadioButton;
            if (radio == null || !(radio.Tag is int)) return;

            int index = (int)radio.Tag;
            if (index < 0 || index >= presets.Count) return;

            MainSubPreset preset = presets[index];
            MainBox.Text = preset.Main ?? "";
            SubBox.Text = preset.Sub ?? "";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string main = (MainBox.Text ?? "").Trim();
            string sub = (SubBox.Text ?? "").Trim();

            if (main.Length == 0 || sub.Length == 0)
            {
                MessageBox.Show(this, "Main 과 Sub 문자열을 모두 입력하세요.", "입력 확인",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MainText = main;
            SubText = sub;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
