namespace FBXtoRVT.Core
{
    /// <summary>
    /// 여러 기능이 함께 쓰는 "패밀리명 키워드" 모음.
    ///
    /// 같은 문자열("FLANGE" 등)을 파일마다 따로 적어 두면, 한쪽만 고쳤을 때
    /// 기능별로 대상이 달라지는 사고가 난다. 그래서 한 곳에 모아 둔다.
    /// (모두 대소문자를 무시하고 "포함되면 대상" 으로 검사한다)
    /// </summary>
    public static class FamilyKeywords
    {
        /// <summary>타공 슬리브 조정의 대상 슬리브</summary>
        public const string Sleeve = "타공 SLEEVE";

        /// <summary>타공 슬리브 조정에서 지우는 플랜지</summary>
        public const string DcFlange = "DC FLANGE";

        /// <summary>플랜지 전체(NW / DC / BLIND 를 모두 포함)</summary>
        public const string Flange = "FLANGE";

        /// <summary>너트</summary>
        public const string Nut = "NUT";

        /// <summary>벨로우즈. 플랜지 상/하 해제 규칙이 반대가 되는 부품</summary>
        public const string Bellows = "BELLOWS";

        /// <summary>엘보</summary>
        public const string Elbow = "ELBOW";

        /// <summary>호퍼</summary>
        public const string Hopper = "HOPPER";

        /// <summary>스크러버(SCR 장비)</summary>
        public const string Scrubber = "SCRUBBER";

        /// <summary>
        /// ELBOW + 어댑터 + 클램프가 함께 들어있는 조립 패밀리.
        /// 플랜지가 아니라 어댑터/클램프 파라미터를 갖고 있어서, 이름 전체를 그대로 적는다.
        /// </summary>
        public const string ElbowAdptAssembly = "ASSEMBLY_ELBOW_ADPT_LOT-FLON";

        // ===== 플랜지 종류 구분용 =====

        /// <summary>NW 플랜지</summary>
        public const string NwFlange = "NW";

        /// <summary>DC 플랜지</summary>
        public const string DcFlangeKind = "DC";

        /// <summary>블라인드 플랜지(파라미터를 건드리지 않는 종류)</summary>
        public const string BlindFlange = "BLIND";
    }

    /// <summary>
    /// 여러 기능이 함께 쓰는 "패밀리 파라미터 이름" 모음.
    ///
    /// 상/하 한 쌍으로 쓰는 파라미터는 <see cref="PartSideTable"/> 에서 짝지어 쓴다.
    /// 이름이 한 글자라도 다르면 Revit 이 파라미터를 못 찾아 <b>조용히 아무 일도 일어나지 않으므로</b>,
    /// 여기 적힌 문자열은 패밀리의 실제 파라미터 이름과 정확히 같아야 한다.
    /// (ADAPTOR 는 밑줄, CLAMP 는 띄어쓰기인 점에 주의)
    /// </summary>
    public static class ParamNames
    {
        /// <summary>플랜지 아래쪽 형상 표시 여부(YES/NO)</summary>
        public const string FlangeLower = "FLANGE 하";

        /// <summary>플랜지 위쪽 형상 표시 여부(YES/NO)</summary>
        public const string FlangeUpper = "FLANGE 상";

        /// <summary>어댑터 아래쪽 형상 표시 여부(YES/NO)</summary>
        public const string AdaptorLower = "ADAPTOR_하";

        /// <summary>어댑터 위쪽 형상 표시 여부(YES/NO)</summary>
        public const string AdaptorUpper = "ADAPTOR_상";

        /// <summary>클램프 아래쪽 형상 표시 여부(YES/NO)</summary>
        public const string ClampLower = "CLAMP 하";

        /// <summary>클램프 위쪽 형상 표시 여부(YES/NO)</summary>
        public const string ClampUpper = "CLAMP 상";

        /// <summary>구경(호칭지름)</summary>
        public const string Nd1 = "ND1";
    }
}
