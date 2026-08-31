using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>플랜지의 위/아래 구분.</summary>
    public enum FlangeSide
    {
        /// <summary>해제할 플랜지가 없는 종류 (BLIND 처럼 한쪽뿐인 것)</summary>
        None,

        /// <summary>"FLANGE 상"</summary>
        Upper,

        /// <summary>"FLANGE 하"</summary>
        Lower
    }

    /// <summary>
    /// <b>패밀리별로 Primary 커넥터가 어느 쪽(상/하)인지</b> 적어 두는 표.
    ///
    /// [왜 이렇게 하나]
    /// 플랜지를 무언가에 붙일 때 하는 일은 어느 기능에서나 똑같다.
    ///
    ///   <b>지금 붙이는 커넥터 쪽 플랜지를 해제한다.</b>
    ///
    /// 그런데 "지금 쓰는 커넥터가 상이냐 하냐" 는 패밀리마다 다르다.
    /// 예전에는 그 판단을 기능마다 따로 적어 두어서, 같은 DC FLANGE 인데
    /// 장비&amp;플랜지 기능은 "하" 를, HOPPER&amp;플랜지 기능은 "상" 을 해제하는 식으로
    /// 규칙이 갈라져 있었다.
    ///
    /// 그래서 <b>패밀리별 정보는 이 표에만</b> 두고, 각 기능은
    /// "Primary 커넥터를 쓰는가, 아닌가" 만 알면 되도록 했다.
    ///
    /// [새 플랜지 패밀리가 생기면]
    /// 아래 <c>Table</c> 에 한 줄만 추가하면 모든 기능에 함께 적용된다.
    /// </summary>
    public static class FlangeSideTable
    {
        /// <summary>
        /// 패밀리(또는 타입) 이름 키워드 → 그 패밀리의 <b>Primary 커넥터가 있는 쪽</b>.
        ///
        /// <b>위에서부터 검사해서 먼저 걸리는 줄을 쓴다.</b>
        /// (이름에 두 키워드가 다 들어있을 때 어느 쪽이 이기는지가 표의 순서로 정해진다.
        ///  예: "BLIND DC FLANGE" 는 BLIND 가 위에 있으므로 None 이 된다)
        /// </summary>
        private static readonly (string Keyword, FlangeSide PrimarySide)[] Table = new[]
        {
            // 한쪽에만 플랜지가 있어 해제할 것이 없는 종류를 가장 먼저 거른다.
            (FamilyKeywords.BlindFlange,  FlangeSide.None),

            (FamilyKeywords.Bellows,      FlangeSide.Upper),
            (FamilyKeywords.DcFlangeKind, FlangeSide.Upper),
            (FamilyKeywords.NwFlange,     FlangeSide.Lower),
        };

        /// <summary>
        /// 표에 없는 이름일 때 쓸 값.
        /// <b>None</b> = 모르는 패밀리는 파라미터를 건드리지 않는다.
        /// (짐작으로 상/하를 해제하면 엉뚱한 형상이 사라질 수 있으므로, 모를 때는 아무것도 하지 않는다)
        /// </summary>
        private const FlangeSide DefaultPrimarySide = FlangeSide.None;

        /// <summary>
        /// 이 플랜지의 Primary 커넥터가 어느 쪽인지 표에서 찾는다.
        ///
        /// <b>패밀리명을 먼저 보고, 패밀리명에서 못 찾았을 때만 타입명을 본다.</b>
        /// (패밀리명이 더 정확한 정보이므로, 패밀리명이 무언가에 걸리면 그것으로 확정한다)
        /// </summary>
        public static FlangeSide GetPrimarySide(Element flange)
        {
            // 1) 패밀리명으로 찾기
            foreach ((string keyword, FlangeSide primarySide) in Table)
            {
                if (ElementUtils.FamilyNameContains(flange, keyword)) return primarySide;
            }

            // 2) 패밀리명에서 못 찾았을 때만 타입명으로 한 번 더 찾기
            foreach ((string keyword, FlangeSide primarySide) in Table)
            {
                if (ElementUtils.TypeNameContains(flange, keyword)) return primarySide;
            }

            return DefaultPrimarySide;
        }

        /// <summary>
        /// 해제할 파라미터 이름을 돌려준다. 해제할 것이 없으면 null.
        ///
        /// 규칙은 하나뿐이다. <b>지금 붙이는 커넥터 쪽 플랜지를 해제한다.</b>
        /// </summary>
        /// <param name="flange">대상 플랜지</param>
        /// <param name="usingPrimary">지금 붙이는 커넥터가 Primary 커넥터인지</param>
        public static string GetParamToUncheck(Element flange, bool usingPrimary)
        {
            FlangeSide primarySide = GetPrimarySide(flange);
            if (primarySide == FlangeSide.None) return null;   // 모르는 패밀리 / BLIND 는 건드리지 않는다

            // Primary 를 쓰면 Primary 쪽을, 아니면 그 반대쪽을 해제한다.
            FlangeSide sideToUncheck = usingPrimary ? primarySide : Opposite(primarySide);

            return (sideToUncheck == FlangeSide.Upper) ? ParamNames.FlangeUpper : ParamNames.FlangeLower;
        }

        /// <summary>상 ↔ 하 를 뒤집는다.</summary>
        private static FlangeSide Opposite(FlangeSide side)
        {
            if (side == FlangeSide.Upper) return FlangeSide.Lower;
            if (side == FlangeSide.Lower) return FlangeSide.Upper;

            return FlangeSide.None;
        }
    }
}
