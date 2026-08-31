using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// "LINK ON/OFF" 버튼(단축키용)이 실행하는 명령.
    /// 현재 뷰에서 좌표조정 모델(Coordination Model, Navisworks .nwc/.nwd 링크)의
    /// 가시성을 켜짐/꺼짐 토글한다. RVT 링크는 건드리지 않는다.
    /// 단축키로 반복 실행하는 용도이므로 대화상자 없이 조용히 토글만 한다.
    /// (문서/뷰 확인, Transaction, 예외 처리는 <see cref="ViewCommandBase"/> 가 담당한다)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LinkVisibilityCommand : ViewCommandBase
    {
        protected override string FeatureTitle
        {
            get { return "LINK ON/OFF"; }
        }

        protected override string RunInTransaction(Document doc, View view)
        {
            LinkVisibilityHelper.ToggleLinkVisibility(doc, view);

            // null 을 돌려주면 결과 대화상자를 띄우지 않는다. (조용히 토글만 하는 기능)
            return null;
        }
    }
}
