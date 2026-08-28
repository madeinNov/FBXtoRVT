using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT.Commands
{
    /// <summary>
    /// 응원 버튼들의 공통 로직.
    /// 버튼마다 이름만 다르게 지정하면, 그 이름으로 랜덤 응원 문구를 띄운다.
    /// </summary>
    public abstract class CheerCommandBase : IExternalCommand
    {
        // 각 버튼(자식 클래스)에서 지정할 이름
        protected abstract string PersonName { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            CheerMessages.Result cheer = CheerMessages.GetRandom(PersonName);

            // 당첨(1%)일 때만 제목 우측에 당첨 확률 표시, 일반 문구는 제목만
            string title = "응원";
            if (cheer.IsJackpot)
            {
                title = "응원          당첨 확률 " + CheerMessages.JackpotPercentText;
            }

            TaskDialog.Show(title, cheer.Message);
            return Result.Succeeded;
        }
    }

    // 아래는 이름별 버튼 명령. 각 버튼의 FullClassName 이 이 클래스들을 가리킨다.

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerYusam : CheerCommandBase
    {
        protected override string PersonName => "유샘";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerKwonSoonyoung : CheerCommandBase
    {
        protected override string PersonName => "권순영";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerChoiJaewon : CheerCommandBase
    {
        protected override string PersonName => "최재원";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerKimSungmin : CheerCommandBase
    {
        protected override string PersonName => "김성민";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerLeeJonghun : CheerCommandBase
    {
        protected override string PersonName => "이종훈";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerMoonHyunguk : CheerCommandBase
    {
        protected override string PersonName => "문현국";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerKoSeunghee : CheerCommandBase
    {
        protected override string PersonName => "고승희";
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class CheerJungchan : CheerCommandBase
    {
        protected override string PersonName => "정찬";
    }
}
