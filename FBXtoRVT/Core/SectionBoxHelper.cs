using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 선택한 객체들을 감싸는 Section Box 를 3D 뷰에 적용하는 로직.
    /// 지정한 tolerance(mm) 만큼 모든 방향으로 여유를 준다.
    /// </summary>
    public static class SectionBoxHelper
    {
        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int UsedElementCount;  // 바운딩 박스 계산에 실제로 사용된 객체 수
            public bool Applied;          // Section Box 적용 여부
        }

        /// <summary>
        /// 선택 객체들의 합쳐진 바운딩 박스에 tolerance 를 더해 Section Box 적용.
        /// (외부에서 Transaction 을 열고 호출)
        /// </summary>
        public static RunResult Apply(Document doc, View3D view3D, ICollection<ElementId> elementIds, int toleranceMm)
        {
            var result = new RunResult();

            // 1) 선택 객체들의 월드 좌표 바운딩 박스를 하나로 합침
            //    (회전된 박스도 맞도록 계산하는 일은 ElementUtils.GetWorldBox 가 처리한다)
            ElementUtils.WorldBox merged = null;

            foreach (ElementId id in elementIds)
            {
                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(doc.GetElement(id));
                if (box == null) continue;

                merged = (merged == null) ? box : merged.Union(box);
                result.UsedElementCount++;
            }

            // 계산할 박스가 없으면 종료
            if (merged == null)
                return result;

            // 2) tolerance 만큼 모든 방향으로 확장
            ElementUtils.WorldBox expanded = merged.ExpandAll(ElementUtils.MmToFeet(toleranceMm));

            // 3) Section Box 로 적용 (월드 좌표 기준, Transform 은 단위행렬)
            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = expanded.Min,
                Max = expanded.Max
            };

            view3D.SetSectionBox(sectionBox);   // 이 호출로 Section Box 가 켜짐
            view3D.IsSectionBoxActive = true;   // 확실히 활성화

            result.Applied = true;
            return result;
        }
    }
}
