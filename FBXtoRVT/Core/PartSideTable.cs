using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>부품의 위/아래 구분.</summary>
    public enum PartSide
    {
        /// <summary>해제할 것이 없는 종류 (BLIND 처럼 한쪽뿐인 것 / 표에 없는 것)</summary>
        None,

        /// <summary>위쪽 (상)</summary>
        Upper,

        /// <summary>아래쪽 (하)</summary>
        Lower
    }

    /// <summary>
    /// 상/하 한 쌍으로 붙어 다니는 파라미터 이름.
    /// (예: "FLANGE 상" / "FLANGE 하", "ADAPTOR_상" / "ADAPTOR_하")
    /// </summary>
    public class SideParamPair
    {
        public readonly string Upper;
        public readonly string Lower;

        public SideParamPair(string upper, string lower)
        {
            Upper = upper;
            Lower = lower;
        }

        /// <summary>지정한 쪽의 파라미터 이름. None 이면 null.</summary>
        public string Get(PartSide side)
        {
            if (side == PartSide.Upper) return Upper;
            if (side == PartSide.Lower) return Lower;

            return null;
        }
    }

    /// <summary>
    /// <b>패밀리별로 Primary 커넥터가 어느 쪽(상/하)인지</b>, 그리고
    /// <b>그 패밀리가 어떤 상/하 파라미터를 갖고 있는지</b> 적어 두는 표.
    ///
    /// [왜 이렇게 하나]
    /// 부품을 무언가에 붙일 때 하는 일은 어느 기능에서나 똑같다.
    ///
    ///   <b>지금 붙이는 커넥터 쪽 형상을 해제한다.</b>
    ///
    /// 그런데 "지금 쓰는 커넥터가 상이냐 하냐" 와 "어떤 이름의 파라미터를 끄느냐" 는
    /// 패밀리마다 다르다. 예전에는 그 판단을 기능마다 따로 적어 두어서, 같은 DC FLANGE 인데
    /// 장비&amp;플랜지 기능은 "하" 를, HOPPER&amp;플랜지 기능은 "상" 을 해제하는 식으로
    /// 규칙이 갈라져 있었다.
    ///
    /// 그래서 <b>패밀리별 정보는 이 표에만</b> 두고, 각 기능은
    /// "Primary 커넥터를 쓰는가, 아닌가" 만 알면 되도록 했다.
    ///
    /// [새 패밀리가 생기면]
    /// 아래 <c>Table</c> 에 한 줄만 추가하면 모든 기능에 함께 적용된다.
    /// 파라미터 이름이 FLANGE 가 아니어도 된다. 상/하 한 쌍이기만 하면 된다.
    /// (플랜지는 한 쌍, ELBOW 어댑터 조립품은 어댑터·클램프 두 쌍을 갖고 있다)
    /// </summary>
    public static class PartSideTable
    {
        // ===== 파라미터 쌍 =====
        // 특정 쌍 하나만 다뤄야 하는 기능(예: 엘보 어댑터 생성기)이 있어서 밖에서도 쓸 수 있게 둔다.

        public static readonly SideParamPair FlangePair =
            new SideParamPair(ParamNames.FlangeUpper, ParamNames.FlangeLower);

        public static readonly SideParamPair AdaptorPair =
            new SideParamPair(ParamNames.AdaptorUpper, ParamNames.AdaptorLower);

        public static readonly SideParamPair ClampPair =
            new SideParamPair(ParamNames.ClampUpper, ParamNames.ClampLower);

        // 해제할 파라미터가 없는 종류
        private static readonly SideParamPair[] NoPairs = new SideParamPair[0];

        /// <summary>
        /// 이름 키워드 → (그 패밀리의 <b>Primary 커넥터가 있는 쪽</b>, 해제 대상 파라미터 쌍들).
        ///
        /// <b>위에서부터 검사해서 먼저 걸리는 줄을 쓴다.</b>
        /// (이름에 두 키워드가 다 들어있을 때 어느 쪽이 이기는지가 표의 순서로 정해진다.
        ///  예: "BLIND DC FLANGE" 는 BLIND 가 위에 있으므로 None 이 된다)
        ///
        /// 이름 검사는 "포함" 이다. 대소문자를 무시하고, 이름 어디에 있어도 걸린다.
        /// </summary>
        private static readonly (string Keyword, PartSide PrimarySide, SideParamPair[] Pairs)[] Table = new[]
        {
            // 한쪽에만 형상이 있어 해제할 것이 없는 종류를 가장 먼저 거른다.
            (FamilyKeywords.BlindFlange,     PartSide.None,  NoPairs),

            // ELBOW + 어댑터 + 클램프 조립품. 상/하 쌍을 두 개 갖고 있다.
            (FamilyKeywords.ElbowAdptAssembly, PartSide.Upper, new[] { AdaptorPair, ClampPair }),

            (FamilyKeywords.Bellows,         PartSide.Upper, new[] { FlangePair }),
            (FamilyKeywords.DcFlangeKind,    PartSide.Upper, new[] { FlangePair }),
            (FamilyKeywords.NwFlange,        PartSide.Lower, new[] { FlangePair }),
        };

        /// <summary>
        /// 표에 없는 이름일 때 쓸 값.
        /// <b>None</b> = 모르는 패밀리는 파라미터를 건드리지 않는다.
        /// (짐작으로 상/하를 해제하면 엉뚱한 형상이 사라질 수 있으므로, 모를 때는 아무것도 하지 않는다)
        /// </summary>
        private const PartSide DefaultPrimarySide = PartSide.None;

        /// <summary>
        /// 이 부품의 Primary 커넥터가 어느 쪽인지 표에서 찾는다.
        ///
        /// <b>패밀리명을 먼저 보고, 패밀리명에서 못 찾았을 때만 타입명을 본다.</b>
        /// (패밀리명이 더 정확한 정보이므로, 패밀리명이 무언가에 걸리면 그것으로 확정한다)
        /// </summary>
        public static PartSide GetPrimarySide(Element part)
        {
            int row = FindRow(part);
            return (row >= 0) ? Table[row].PrimarySide : DefaultPrimarySide;
        }

        /// <summary>
        /// <b>어떤 커넥터가 이 부품의 어느 쪽(상/하)에 있는지</b> 알려준다.
        /// 표에 없는 패밀리면 None.
        ///
        /// 이 부품의 상/하를 알아야 하는 기능은 모두 이 함수를 거친다.
        /// (해제할 때든 체크할 때든, "어느 쪽이냐" 를 판단하는 곳은 여기 한 곳뿐이다)
        /// </summary>
        /// <param name="part">대상 부품</param>
        /// <param name="isPrimaryConnector">그 커넥터가 Primary 커넥터인지</param>
        public static PartSide GetSideOfConnector(Element part, bool isPrimaryConnector)
        {
            PartSide primarySide = GetPrimarySide(part);
            if (primarySide == PartSide.None) return PartSide.None;

            return isPrimaryConnector ? primarySide : Opposite(primarySide);
        }

        /// <summary>
        /// 해제할 파라미터 이름들을 돌려준다. 해제할 것이 없으면 빈 목록.
        ///
        /// 규칙은 하나뿐이다. <b>지금 붙이는 커넥터 쪽 형상을 해제한다.</b>
        /// 파라미터 쌍이 여러 개인 패밀리는 <b>모든 쌍에서 같은 쪽</b>을 해제한다.
        /// </summary>
        /// <param name="part">대상 부품</param>
        /// <param name="usingPrimary">지금 붙이는 커넥터가 Primary 커넥터인지</param>
        public static List<string> GetParamsToUncheck(Element part, bool usingPrimary)
        {
            var names = new List<string>();

            int row = FindRow(part);
            if (row < 0) return names;                          // 표에 없는 패밀리

            PartSide sideToUncheck = GetSideOfConnector(part, usingPrimary);
            if (sideToUncheck == PartSide.None) return names;   // BLIND 처럼 해제할 것이 없는 종류

            foreach (SideParamPair pair in Table[row].Pairs)
            {
                string name = pair.Get(sideToUncheck);
                if (name != null) names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// 표에서 이 부품에 해당하는 줄 번호를 찾는다. 없으면 -1.
        /// 패밀리명으로 먼저 훑고, 거기서 못 찾았을 때만 타입명으로 한 번 더 훑는다.
        /// </summary>
        private static int FindRow(Element part)
        {
            for (int i = 0; i < Table.Length; i++)
            {
                if (ElementUtils.FamilyNameContains(part, Table[i].Keyword)) return i;
            }

            for (int i = 0; i < Table.Length; i++)
            {
                if (ElementUtils.TypeNameContains(part, Table[i].Keyword)) return i;
            }

            return -1;
        }

        /// <summary>상 ↔ 하 를 뒤집는다.</summary>
        private static PartSide Opposite(PartSide side)
        {
            if (side == PartSide.Upper) return PartSide.Lower;
            if (side == PartSide.Lower) return PartSide.Upper;

            return PartSide.None;
        }
    }
}
