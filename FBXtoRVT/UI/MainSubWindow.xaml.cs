using System.Windows;

namespace FBXtoRVT.UI
{
    /// <summary>
    /// Main / Sub 문자열 2개를 입력받는 WPF 창.
    /// 생성 시 각 칸의 기본값을 지정할 수 있다.
    /// </summary>
    public partial class MainSubWindow : Window
    {
        // 명령 쪽에서 읽어갈 결과 값
        public string MainText { get; private set; }
        public string SubText { get; private set; }

        /// <param name="title">창 제목</param>
        /// <param name="mainDefault">Main 칸 기본값</param>
        /// <param name="subDefault">Sub 칸 기본값</param>
        public MainSubWindow(string title, string mainDefault, string subDefault)
        {
            InitializeComponent();

            Title = title;
            MainBox.Text = mainDefault ?? "";
            SubBox.Text = subDefault ?? "";
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
