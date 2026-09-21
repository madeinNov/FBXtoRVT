using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "엘보 어댑터 생성기" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// 양쪽이 모두 연결된 엘보 조립품에서, SCR 장비 반대쪽(= 배관 쪽) ADAPTOR 를 켠다.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElbowAdapterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

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

            try
            {
                // 2) 트랜잭션 안에서 실행 (ADAPTOR 파라미터 체크)
                ElbowAdapterHelper.RunResult r;

                using (Transaction tx = new Transaction(doc, "엘보 ADAPTOR 체크"))
                {
                    tx.Start();
                    r = ElbowAdapterHelper.Run(doc, activeView);
                    tx.Commit();
                }

                // 3) 결과 요약 표시
                string summary =
                    $"대상 엘보: {r.ElbowCount}개\n" +
                    $"SCR 장비: {r.ScrubberCount}대\n\n" +
                    $"ADAPTOR 체크: {r.CheckedCount}개\n" +
                    $"이미 체크돼 있음: {r.AlreadyCheckedCount}개\n\n" +
                    $"건너뜀(커넥터가 열려 있음): {r.SkippedOpenCount}개\n" +
                    $"건너뜀(먼 쪽이 배관이 아님): {r.SkippedNotPipeCount}개\n" +
                    $"건너뜀(커넥터가 2개가 아님): {r.SkippedNotTwoCount}개";

                // 파라미터를 못 찾은 경우는 이름이 틀렸을 가능성이 크므로 눈에 띄게 알려준다.
                if (r.ParamNotFoundCount > 0)
                {
                    summary += $"\n\n[ADAPTOR 파라미터를 찾지 못함: {r.ParamNotFoundCount}개]\n" +
                               "패밀리의 실제 파라미터 이름이 'ADAPTOR_상' / 'ADAPTOR_하' 와 같은지 확인하세요.";
                }

                // SCR 장비가 없으면 어느 쪽이 바깥인지 정할 수 없어 아무것도 하지 않는다.
                if (r.ScrubberCount == 0)
                {
                    summary += "\n\n현재 뷰에 SCR 장비(SCRUBBER)가 없어 아무것도 하지 않았습니다.";
                }

                TaskDialog.Show(ElbowAdapterHelper.FeatureName, summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "엘보 어댑터 생성기 실행 실패");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
