using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "HOPPER&amp;플랜지" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// HOPPER 바운딩 박스(+50mm) 안에 FLANGE 가 딱 1개일 때, 파라미터를 정리하고 HOPPER 를 연결한다.
    /// (문서/뷰 확인, Transaction, 예외 처리는 <see cref="ViewCommandBase"/> 가 담당한다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class HopperFlangeCommand : ViewCommandBase
    {
        protected override string FeatureTitle
        {
            get { return "HOPPER&플랜지"; }
        }

        protected override string TransactionName
        {
            get { return "HOPPER 플랜지 연결"; }
        }

        protected override string RunInTransaction(Document doc, View view)
        {
            HopperFlangeHelper.RunResult r = HopperFlangeHelper.Run(doc, view);

            return
                $"HOPPER: {r.HopperCount}개\n" +
                $"연결 대상 FLANGE: {r.TargetFlangeCount}개\n" +
                $"박스 안 FLANGE 가 1개가 아니라 건너뜀: {r.SkippedCount}개\n\n" +
                $"ND1 복사: {r.Nd1CopiedCount}건\n" +
                $"ND1 미적용(HOPPER 커넥터 ND 불일치): {r.Nd1SkippedMixedCount}건\n" +
                $"파라미터 해제: {r.ParamUncheckedCount}건\n" +
                $"연결 성공: {r.ConnectedCount}건\n" +
                $"연결 실패: {r.FailedCount}건";
        }
    }
}
