using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "장비&amp;플랜지/NUT" 기능.
    ///
    /// 대상은 <b>Mechanical Equipment 카테고리 전체</b>이고,
    /// 장비 바운딩 박스를 모든 방향으로 20mm 키워서 쓴다.
    /// 그 밖의 규칙(부품을 어떻게 고르고 어떤 파라미터를 해제해 연결하는지)은
    /// <see cref="FlangeNutAttachHelper"/> 에 모아 두었다.
    /// (같은 규칙을 쓰는 "SCR장비&amp;플랜지/NUT" 은 <see cref="ScrubberFlangeHelper"/>)
    /// </summary>
    public static class EquipmentFlangeNutHelper
    {
        // 기능 이름 (결과 대화상자 제목 / 로그에 함께 쓴다)
        public const string FeatureName = "장비&플랜지/NUT";

        // 장비 바운딩 박스 확장량(mm). 모든 방향(X/Y/Z 앞뒤)으로 이만큼 키운다.
        private const double EquipBoxExpandMm = 20.0;

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static FlangeNutAttachHelper.RunResult Run(Document doc, View view)
        {
            // 처리 도중 부품이 이동하므로, 장비는 Id 목록으로 먼저 확정해 둔다.
            var equipIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstancesByCategory(
                doc, view, BuiltInCategory.OST_MechanicalEquipment))
            {
                equipIds.Add(fi.Id);
            }

            return FlangeNutAttachHelper.Run(
                doc, view, equipIds, ElementUtils.MmToFeet(EquipBoxExpandMm), FeatureName);
        }
    }
}
