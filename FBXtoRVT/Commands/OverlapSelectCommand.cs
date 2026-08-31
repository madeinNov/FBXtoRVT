using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;
using FBXtoRVT.UI;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "겹침 객체 선택" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// Main 객체의 바운딩 박스 안에 중심점이 들어가는(= 겹치는) Sub 객체를 선택한다.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class OverlapSelectCommand : IExternalCommand
    {
        // 창 제목 겸 결과 대화상자 제목
        private const string FeatureTitle = "겹침 객체 선택";

        // 입력창 위쪽 프리셋(라디오) 목록.
        // 3~5번은 아직 정하지 않았으므로 선택 불가 상태로 자리만 만들어 둔다.
        // (나중에 문자열만 채우고 마지막 인자를 true 로 바꾸면 바로 쓸 수 있다)
        private static readonly MainSubPreset[] Presets = new[]
        {
            new MainSubPreset("ADPT",
                "ASSEMBLY_ELBOW_ADPT_LOT-FLON",
                "ASSEMBLY_DC CLAMP_ADAPTOR_ADPT_LOT-FLON"),

            new MainSubPreset("BELLOWS",
                "BELLOWS",
                "FLANGE"),

            new MainSubPreset("미정3", "", "", false),
            new MainSubPreset("미정4", "", "", false),
            new MainSubPreset("미정5", "", "", false),
        };

        // 창을 열었을 때 처음 골라 둘 프리셋 번호 (0 = ADPT)
        private const int DefaultPresetIndex = 0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            // 1) 열린 문서 확인
            if (uiDoc == null || uiDoc.Document == null)
            {
                message = "열린 문서가 없습니다.";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;
            View activeView = doc.ActiveView;
            if (activeView == null)
            {
                message = "활성 뷰가 없습니다.";
                return Result.Failed;
            }

            // 2) Main / Sub 입력창 표시 (위쪽 프리셋 라디오로 자주 쓰는 조합을 고를 수 있다)
            var window = new MainSubWindow(FeatureTitle, Presets, DefaultPresetIndex);
            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

            bool? dialogResult = window.ShowDialog();
            if (dialogResult != true)
            {
                return Result.Cancelled;
            }

            string mainKeyword = window.MainText;
            string subKeyword = window.SubText;

            try
            {
                // 3) 로직 실행 (선택은 UI 작업이므로 트랜잭션 불필요)
                OverlapSelectHelper.RunResult runResult =
                    OverlapSelectHelper.Run(doc, activeView, mainKeyword, subKeyword);

                // 4) 대상 객체 선택
                uiDoc.Selection.SetElementIds(runResult.Selected);

                // 5) 결과 요약 표시
                string summary =
                    $"Main('{mainKeyword}') 객체: {runResult.MainCount}개\n" +
                    $"Sub('{subKeyword}') 객체: {runResult.SubCount}개\n\n" +
                    $"Main 과 겹쳐서 선택된 Sub 객체: {runResult.Selected.Count}개";

                // Main 이 Sub 조건까지 만족한 경우가 있으면, 왜 뺐는지 같이 알려준다
                if (runResult.MainAlsoMatchedSubCount > 0)
                {
                    summary += $"\n(Sub 조건도 만족했지만 Main 이라 제외: {runResult.MainAlsoMatchedSubCount}개)";
                }

                TaskDialog.Show(FeatureTitle, summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
