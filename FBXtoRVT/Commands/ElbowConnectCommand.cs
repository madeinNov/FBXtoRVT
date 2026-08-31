using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "ELBOW&amp;배관/플랜지" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// 엘보의 열린 커넥터 주변 60mm 안의 FLANGE / 배관 끝점을 찾아 연결한다.
    /// (문서/뷰 확인, Transaction, 예외 처리는 <see cref="ViewCommandBase"/> 가 담당한다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElbowConnectCommand : ViewCommandBase
    {
        protected override string FeatureTitle
        {
            get { return "ELBOW&배관/플랜지"; }
        }

        protected override string TransactionName
        {
            get { return "ELBOW 배관/플랜지 연결"; }
        }

        protected override string RunInTransaction(Document doc, View view)
        {
            ElbowConnectHelper.RunResult r = ElbowConnectHelper.Run(doc, view);

            return
                $"열린 커넥터가 있는 ELBOW: {r.ElbowCount}개\n\n" +
                $"엘보에 붙인 FLANGE: {r.FlangeConnectedCount}건\n" +
                $"배관 연결: {r.PipeConnectedCount}건\n" +
                $"연결 실패: {r.FailedCount}건";
        }
    }
}
