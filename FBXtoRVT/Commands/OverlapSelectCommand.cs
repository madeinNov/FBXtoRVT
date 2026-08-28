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

        // 입력창 기본값
        private const string MainDefault = "ASSEMBLY_ELBOW_ADPT_LOT-FLON";
        private const string SubDefault = "ASSEMBLY_DC CLAMP_ADAPTOR_ADPT_LOT-FLON";

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

            // 2) Main / Sub 입력창 표시
            var window = new MainSubWindow(FeatureTitle, MainDefault, SubDefault);
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
