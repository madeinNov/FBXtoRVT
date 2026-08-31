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
    /// </summary>
    public static class ParamNames
    {
        /// <summary>플랜지 아래쪽 형상 표시 여부(YES/NO)</summary>
        public const string FlangeLower = "FLANGE 하";

        /// <summary>플랜지 위쪽 형상 표시 여부(YES/NO)</summary>
        public const string FlangeUpper = "FLANGE 상";

        /// <summary>구경(호칭지름)</summary>
        public const string Nd1 = "ND1";
    }
}
