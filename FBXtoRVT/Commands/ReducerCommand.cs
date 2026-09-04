using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FBXtoRVT.Core;
using FBXtoRVT.UI;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "Reducer 생성기" 버튼이 실행하는 명령.
    ///
    /// 흐름
    ///   1) 배관을 클릭한다.
    ///   2) 숫자 입력창이 뜬다. (반대쪽 지름 mm, 기본값 50)
    ///   3) 클릭한 지점에 가까운 열린 커넥터 바깥쪽에 오토라우팅 리듀서가 들어가고,
    ///      그 반대쪽에는 입력한 지름의 배관이 100mm 길이로 함께 만들어진다.
    ///      (클릭한 배관의 길이는 변하지 않는다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReducerCommand : IExternalCommand
    {
        // 대화상자 / 입력창 제목
        private const string FeatureTitle = "Reducer 생성기";

        // 입력창 기본값과 허용 범위 (mm)
        private const int DefaultNdMm = 50;
        private const int MinNdMm = 1;
        private const int MaxNdMm = 5000;

        // 마지막으로 입력한 값. Revit 을 켜 둔 동안 기억해서 다음 실행 때 그대로 보여준다.
        private static int lastNdMm = DefaultNdMm;

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

            try
            {
                // 2) 배관 클릭 (배관만 고를 수 있게 필터를 건다)
                Reference pipeRef = uiDoc.Selection.PickObject(
                    ObjectType.Element, new PipeSelectionFilter(),
                    "리듀서를 넣을 배관을, 리듀서를 넣고 싶은 쪽 끝 가까이에서 클릭하세요.");

                var pipe = doc.GetElement(pipeRef) as Pipe;
                if (pipe == null)
                {
                    message = "배관 선택에 실패했습니다.";
                    return Result.Failed;
                }

                // 클릭한 지점: 열린 커넥터가 양쪽에 다 있을 때 어느 쪽인지 고르는 기준이 된다.
                XYZ clickPoint = pipeRef.GlobalPoint;

                // 3) 반대쪽 지름 입력창
                double sourceNdMm = UnitUtils.ConvertFromInternalUnits(pipe.Diameter, UnitTypeId.Millimeters);

                var window = new NumberInputWindow(
                    FeatureTitle,
                    $"클릭한 배관의 지름은 {sourceNdMm:F0}mm 입니다.\n리듀서 반대쪽 지름을 입력하세요.",
                    "지름 (mm)",
                    lastNdMm, MinNdMm, MaxNdMm);

                new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                    return Result.Cancelled;

                lastNdMm = window.Value;   // 다음 실행 때 기본값으로 쓴다

                // 4) 트랜잭션 안에서 리듀서 + 반대쪽 배관 생성
                ReducerHelper.ReducerResult runResult;

                using (Transaction tx = new Transaction(doc, "Reducer 생성"))
                {
                    tx.Start();
                    runResult = ReducerHelper.CreateReducer(doc, pipe, clickPoint, window.Value);
                    tx.Commit();
                }

                // 5) 만들어진 리듀서를 선택 상태로 만들어 바로 확인할 수 있게 한다.
                if (runResult.ReducerId != null && runResult.ReducerId != ElementId.InvalidElementId)
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId> { runResult.ReducerId });
                }

                // 6) 결과 요약 (문제없이 끝났으면 조용히 넘어간다)
                string summary = BuildSummary(runResult);
                if (summary != null) TaskDialog.Show(FeatureTitle, summary);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 사용자가 ESC 로 취소
                return Result.Cancelled;
            }
            catch (InvalidOperationException ex)
            {
                // 열린 커넥터 없음 / 지원하지 않는 사이즈 등 조건 불충족: 안내 후 종료 (트랜잭션은 롤백됨)
                TaskDialog.Show(FeatureTitle, ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "Reducer 생성기 실행 실패");
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// 결과 요약 문구를 만든다.
        /// 배관 끝점이 그대로 유지됐고 리듀서도 잘 들어갔으면 알려줄 것이 없으므로 null 을 돌려 창을 띄우지 않는다.
        /// </summary>
        private static string BuildSummary(ReducerHelper.ReducerResult r)
        {
            if (!r.PipeEndRestored) return null;

            return $"리듀서를 넣었습니다. ({r.SourceNdMm:F0}mm → {r.TargetNdMm:F0}mm, 길이 {r.ReducerLengthMm:F0}mm)\n\n" +
                   "리듀서를 넣으면서 클릭한 배관의 끝점이 움직여서, 원래 위치로 되돌렸습니다.\n" +
                   "배관과 리듀서가 제대로 이어졌는지 한 번 확인해 주세요.";
        }
    }
}
