using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "SCR장비&amp;플랜지/NUT" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// (문서/뷰 확인, Transaction, 예외 처리는 <see cref="ViewCommandBase"/> 가 담당한다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ScrubberFlangeCommand : ViewCommandBase
    {
        protected override string FeatureTitle
        {
            get { return ScrubberFlangeHelper.FeatureName; }
        }

        protected override string TransactionName
        {
            get { return "SCR장비&플랜지/NUT 연결"; }
        }

        protected override string RunInTransaction(Document doc, View view)
        {
            FlangeNutAttachHelper.RunResult r = ScrubberFlangeHelper.Run(doc, view);

            return
                $"SCRUBBER: {r.EquipmentCount}대\n" +
                $"파라미터 해제: {r.ParamUncheckedCount}개\n\n" +
                $"FLANGE - 대상 인식: {r.FlangeTargetCount}개 / " +
                $"연결 성공: {r.FlangeConnectedCount}개 / 실패: {r.FlangeFailedCount}개\n" +
                $"NUT - 대상 인식: {r.NutTargetCount}개 / " +
                $"연결 성공: {r.NutConnectedCount}개 / 실패: {r.NutFailedCount}개";
        }
    }
}
