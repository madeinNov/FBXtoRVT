using System;
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

            // mm → Revit 내부 단위(feet)
            double tol = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);

            // 1) 선택 객체들의 월드 좌표 바운딩 박스를 하나로 합침
            XYZ min = null;
            XYZ max = null;

            foreach (ElementId id in elementIds)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                BoundingBoxXYZ box = e.get_BoundingBox(null); // 모델 좌표 기준
                if (box == null) continue;

                // 박스가 회전(Transform)돼 있어도 맞도록 8개 꼭짓점을 월드 좌표로 변환해 min/max 갱신
                foreach (XYZ corner in GetCorners(box))
                {
                    XYZ p = box.Transform.OfPoint(corner);
                    if (min == null)
                    {
                        min = p;
                        max = p;
                    }
                    else
                    {
                        min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                        max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                    }
                }

                result.UsedElementCount++;
            }

            // 계산할 박스가 없으면 종료
            if (min == null)
                return result;

            // 2) tolerance 만큼 모든 방향으로 확장
            XYZ expandedMin = new XYZ(min.X - tol, min.Y - tol, min.Z - tol);
            XYZ expandedMax = new XYZ(max.X + tol, max.Y + tol, max.Z + tol);

            // 3) Section Box 로 적용 (월드 좌표 기준, Transform 은 단위행렬)
            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = expandedMin,
                Max = expandedMax
            };

            view3D.SetSectionBox(sectionBox);   // 이 호출로 Section Box 가 켜짐
            view3D.IsSectionBoxActive = true;   // 확실히 활성화

            result.Applied = true;
            return result;
        }

        /// <summary>
        /// 바운딩 박스의 8개 꼭짓점(로컬 좌표)을 반환.
        /// </summary>
        private static IEnumerable<XYZ> GetCorners(BoundingBoxXYZ box)
        {
            XYZ mn = box.Min;
            XYZ mx = box.Max;

            yield return new XYZ(mn.X, mn.Y, mn.Z);
            yield return new XYZ(mx.X, mn.Y, mn.Z);
            yield return new XYZ(mn.X, mx.Y, mn.Z);
            yield return new XYZ(mn.X, mn.Y, mx.Z);
            yield return new XYZ(mx.X, mx.Y, mn.Z);
            yield return new XYZ(mx.X, mn.Y, mx.Z);
            yield return new XYZ(mn.X, mx.Y, mx.Z);
            yield return new XYZ(mx.X, mx.Y, mx.Z);
        }
    }
}
