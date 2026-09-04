using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace FBXtoRVT.UI
{
    /// <summary>
    /// 정수 하나만 입력받는 WPF 창.
    ///
    /// 숫자(0~9)가 아닌 글자는 아예 입력되지 않고, 붙여넣기도 숫자만 허용한다.
    /// OK 를 누르면 <see cref="Value"/> 에 입력값이 들어간다.
    /// </summary>
    public partial class NumberInputWindow : Window
    {
        /// <summary>OK 로 닫았을 때의 입력값</summary>
        public int Value { get; private set; }

        // 허용 범위 (이 범위를 벗어나면 OK 를 눌러도 안내만 하고 닫히지 않는다)
        private readonly int minValue;
        private readonly int maxValue;

        /// <param name="title">창 제목</param>
        /// <param name="hint">입력칸 위에 보여줄 안내 문구 (없으면 빈 문자열)</param>
        /// <param name="label">입력칸 왼쪽 라벨</param>
        /// <param name="defaultValue">처음에 채워 둘 값</param>
        /// <param name="minValue">허용 최소값</param>
        /// <param name="maxValue">허용 최대값</param>
        public NumberInputWindow(string title, string hint, string label,
            int defaultValue, int minValue, int maxValue)
        {
            InitializeComponent();

            Title = title;
            HintText.Text = hint ?? "";
            LabelText.Text = label ?? "";

            this.minValue = minValue;
            this.maxValue = maxValue;

            ValueBox.Text = defaultValue.ToString();

            // 붙여넣기도 숫자만 허용
            DataObject.AddPastingHandler(ValueBox, ValueBox_Pasting);

            // 창이 뜨면 바로 입력칸에 포커스를 주고 전체 선택 (곧바로 덮어쓸 수 있게)
            Loaded += (s, e) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        /// <summary>
        /// 키보드로 숫자가 아닌 글자를 치면 입력 자체를 막는다.
        /// </summary>
        private void ValueBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsDigitsOnly(e.Text);
        }

        /// <summary>
        /// 붙여넣기 내용이 숫자가 아니면 붙여넣기를 취소한다.
        /// </summary>
        private void ValueBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            string pasted = e.DataObject.GetDataPresent(typeof(string))
                ? (string)e.DataObject.GetData(typeof(string))
                : null;

            if (pasted == null || !IsDigitsOnly(pasted))
                e.CancelCommand();
        }

        /// <summary>숫자(0~9)로만 이루어진 문자열인지 검사.</summary>
        private static bool IsDigitsOnly(string text)
        {
            return !string.IsNullOrEmpty(text) && Regex.IsMatch(text, "^[0-9]+$");
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            int value;
            if (!int.TryParse((ValueBox.Text ?? "").Trim(), out value))
            {
                MessageBox.Show(this, "숫자를 입력하세요.", "입력 확인",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (value < minValue || value > maxValue)
            {
                MessageBox.Show(this, $"{minValue} ~ {maxValue} 사이의 값을 입력하세요.", "입력 확인",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Value = value;

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
