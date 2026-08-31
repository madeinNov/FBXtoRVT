using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "타공 슬리브 조정" 버튼이 실행하는 명령.
    /// 조건: 1) 현재 열린 Document 2) 현재 View 에 전시된 객체
    /// (문서/뷰 확인, Transaction, 예외 처리는 <see cref="ViewCommandBase"/> 가 담당한다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SleeveAdjustCommand : ViewCommandBase
    {
        protected override string FeatureTitle
        {
            get { return "타공 슬리브 조정"; }
        }

        protected override string RunInTransaction(Document doc, View view)
        {
            SleeveAdjustHelper.RunResult r = SleeveAdjustHelper.Run(doc, view);

            return
                $"타공 SLEEVE: {r.SleeveCount}개\n" +
                $"삭제한 DC FLANGE: {r.DeletedFlangeCount}개\n\n" +
                $"상부(Primary) 연결: {r.TopConnectedCount}건\n" +
                $"하부 연결: {r.BottomConnectedCount}건\n" +
                $"연결 실패: {r.FailedCount}건\n" +
                $"대상 배관을 못 찾은 슬리브: {r.NoPipeSleeveCount}개";
        }
    }
}
