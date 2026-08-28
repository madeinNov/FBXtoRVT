using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 여러 기능이 함께 쓰는 공통 유틸.
    /// 객체 조회 / 월드 바운딩 박스 / 커넥터 조회를 한 곳에서 처리한다.
    ///
    /// 프로젝트 공통 규칙(docs/PROJECT_RULES.md 규칙 1)인
    /// "복합 패밀리의 Sub-Component 는 기능 대상에서 제외" 를 이 파일의 수집 함수에서 보장한다.
    /// </summary>
    public static class ElementUtils
    {
        /// <summary>
        /// 월드 좌표 기준 축정렬(AABB) 바운딩 박스.
        /// Revit 의 BoundingBoxXYZ 는 Transform 이 붙어 있어 다루기 번거로우므로,
        /// 8개 꼭짓점을 월드로 변환해 min/max 만 남긴 단순한 형태로 쓴다.
        /// </summary>
        public class WorldBox
        {
            public XYZ Min;
            public XYZ Max;

            /// <summary>박스 중심점</summary>
            public XYZ Center
            {
                get { return (Min + Max) * 0.5; }
            }

            /// <summary>상부면(윗면) 중심점</summary>
            public XYZ TopFaceCenter
            {
                get { return new XYZ((Min.X + Max.X) * 0.5, (Min.Y + Max.Y) * 0.5, Max.Z); }
            }

            /// <summary>하부면(아랫면) 중심점</summary>
            public XYZ BottomFaceCenter
            {
                get { return new XYZ((Min.X + Max.X) * 0.5, (Min.Y + Max.Y) * 0.5, Min.Z); }
            }

            /// <summary>점이 박스 안에 있는지 검사(경계 포함).</summary>
            public bool Contains(XYZ p)
            {
                if (p == null) return false;

                return p.X >= Min.X && p.X <= Max.X
                    && p.Y >= Min.Y && p.Y <= Max.Y
                    && p.Z >= Min.Z && p.Z <= Max.Z;
            }

            /// <summary>
            /// 위/아래로만 박스를 키운 새 박스를 반환. (단위: feet)
            /// </summary>
            public WorldBox ExpandVertical(double topFeet, double bottomFeet)
            {
                return new WorldBox
                {
                    Min = new XYZ(Min.X, Min.Y, Min.Z - bottomFeet),
                    Max = new XYZ(Max.X, Max.Y, Max.Z + topFeet)
                };
            }

            /// <summary>
            /// 모든 방향(X/Y/Z 앞뒤)으로 박스를 키운 새 박스를 반환. (단위: feet)
            /// </summary>
            public WorldBox ExpandAll(double feet)
            {
                return new WorldBox
                {
                    Min = new XYZ(Min.X - feet, Min.Y - feet, Min.Z - feet),
                    Max = new XYZ(Max.X + feet, Max.Y + feet, Max.Z + feet)
                };
            }

            /// <summary>
            /// 한 점을 중심으로 하는 정육면체 박스를 만든다.
            /// sizeFeet 는 한 변의 전체 길이(= 중심에서 각 방향으로 절반씩).
            /// </summary>
            public static WorldBox FromCenter(XYZ center, double sizeFeet)
            {
                double half = sizeFeet * 0.5;

                return new WorldBox
                {
                    Min = new XYZ(center.X - half, center.Y - half, center.Z - half),
                    Max = new XYZ(center.X + half, center.Y + half, center.Z + half)
                };
            }
        }

        /// <summary>mm 를 Revit 내부 단위(feet)로 변환.</summary>
        public static double MmToFeet(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        /// <summary>
        /// 점의 Z 값(표고)에 가장 가까운 Level 의 Id 를 반환.
        /// Level 이 하나도 없으면 ElementId.InvalidElementId.
        /// </summary>
        public static ElementId FindNearestLevelId(Document doc, XYZ point)
        {
            Level best = null;
            double bestDist = double.MaxValue;

            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                double dist = Math.Abs(level.Elevation - point.Z);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = level;
                }
            }

            return best != null ? best.Id : ElementId.InvalidElementId;
        }

        // ===== 객체 조회 =====

        /// <summary>
        /// 복합 패밀리 안에 들어있는 Sub-Component 인지 검사.
        /// SuperComponent(부모)가 있으면 Sub-Component 이므로 기능 대상에서 제외한다.
        /// </summary>
        public static bool IsSubComponent(Element e)
        {
            var fi = e as FamilyInstance;
            return fi != null && fi.SuperComponent != null;
        }

        /// <summary>
        /// 패밀리명(Family.Name)에 키워드가 포함되는지(대소문자 무시).
        /// </summary>
        public static bool FamilyNameContains(Element e, string keyword)
        {
            var fi = e as FamilyInstance;
            if (fi == null || fi.Symbol == null || fi.Symbol.Family == null) return false;

            string familyName = fi.Symbol.Family.Name ?? "";
            return familyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 현재 뷰에서 패밀리명에 키워드가 포함된 FamilyInstance 를 수집.
        /// 복합 패밀리의 Sub-Component 는 제외한다.
        /// </summary>
        public static IEnumerable<FamilyInstance> CollectFamilyInstances(Document doc, View view, string familyKeyword)
        {
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance));

            foreach (Element e in collector)
            {
                if (IsSubComponent(e)) continue;              // 규칙 1: 내부 부품은 대상 아님
                if (!FamilyNameContains(e, familyKeyword)) continue;

                yield return (FamilyInstance)e;
            }
        }

        /// <summary>
        /// 현재 뷰에서 지정한 카테고리의 FamilyInstance 를 수집.
        /// 복합 패밀리의 Sub-Component 는 제외한다.
        /// </summary>
        public static IEnumerable<FamilyInstance> CollectFamilyInstancesByCategory(Document doc, View view, BuiltInCategory category)
        {
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfCategory(category)
                .OfClass(typeof(FamilyInstance));

            foreach (Element e in collector)
            {
                if (IsSubComponent(e)) continue;              // 규칙 1: 내부 부품은 대상 아님

                yield return (FamilyInstance)e;
            }
        }

        // ===== 바운딩 박스 =====

        /// <summary>
        /// 객체의 월드 좌표 기준 축정렬 바운딩 박스를 계산. 박스가 없으면 null.
        /// 박스가 회전(Transform)돼 있어도 맞도록 8개 꼭짓점을 월드로 변환해 min/max 를 구한다.
        /// </summary>
        public static WorldBox GetWorldBox(Element e)
        {
            if (e == null) return null;

            BoundingBoxXYZ box = e.get_BoundingBox(null); // 모델(월드) 좌표 기준
            if (box == null) return null;

            XYZ min = null;
            XYZ max = null;

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

            return new WorldBox { Min = min, Max = max };
        }

        /// <summary>
        /// 객체의 중심점(월드 바운딩 박스 중앙)을 반환.
        /// 바운딩 박스가 없으면 위치점(LocationPoint)으로 대체하고, 그것도 없으면 null.
        /// </summary>
        public static XYZ GetCenter(Element e)
        {
            WorldBox box = GetWorldBox(e);
            if (box != null) return box.Center;

            var lp = (e != null) ? e.Location as LocationPoint : null;
            return (lp != null) ? lp.Point : null;
        }

        /// <summary>바운딩 박스의 8개 꼭짓점(로컬 좌표)을 반환.</summary>
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

        // ===== 커넥터 =====

        /// <summary>
        /// 객체에서 ConnectorManager 를 안전하게 가져온다. (패밀리 / 배관 등 MEPCurve 모두 지원)
        /// </summary>
        public static ConnectorManager GetConnectorManager(Element e)
        {
            if (e is FamilyInstance fi && fi.MEPModel != null)
                return fi.MEPModel.ConnectorManager;

            if (e is MEPCurve mc)
                return mc.ConnectorManager;

            return null;
        }

        /// <summary>
        /// 물리적으로 연결 가능한 End 커넥터 전체를 반환.
        /// </summary>
        public static List<Connector> GetEndConnectors(Element e)
        {
            var list = new List<Connector>();

            ConnectorManager cm = GetConnectorManager(e);
            if (cm == null) return list;

            foreach (Connector c in cm.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                list.Add(c);
            }

            return list;
        }

        /// <summary>
        /// 아직 연결되지 않은(열린) End 커넥터만 반환.
        /// </summary>
        public static List<Connector> GetOpenEndConnectors(Element e)
        {
            var list = new List<Connector>();

            foreach (Connector c in GetEndConnectors(e))
            {
                if (c.IsConnected) continue; // 이미 연결된(닫힌) 커넥터 제외
                list.Add(c);
            }

            return list;
        }

        /// <summary>
        /// 커넥터가 Primary 커넥터인지 검사.
        /// Primary 여부는 Connector 자체가 아니라 MEPConnectorInfo 에 들어있다.
        /// </summary>
        public static bool IsPrimaryConnector(Connector c)
        {
            if (c == null) return false;

            MEPConnectorInfo info = c.GetMEPConnectorInfo();
            return info != null && info.IsPrimary;
        }

        /// <summary>
        /// 객체의 Primary 커넥터를 반환. 없으면 null.
        /// </summary>
        public static Connector GetPrimaryConnector(Element e)
        {
            foreach (Connector c in GetEndConnectors(e))
            {
                if (IsPrimaryConnector(c)) return c;
            }

            return null;
        }

        /// <summary>
        /// 객체 Id + 커넥터 Id 로 커넥터를 다시 찾아온다.
        /// 삭제 / 이동 / 파라미터 변경 뒤에는 기존 Connector 객체가 낡은 값을 가질 수 있으므로,
        /// 실제로 쓰기 직전에 이 함수로 다시 조회한다. 못 찾으면 null.
        /// </summary>
        public static Connector ResolveConnector(Document doc, ElementId ownerId, int connectorId)
        {
            Element owner = doc.GetElement(ownerId);
            if (owner == null) return null;

            foreach (Connector c in GetEndConnectors(owner))
            {
                if (c.Id == connectorId) return c;
            }

            return null;
        }

        /// <summary>
        /// YES/NO 인스턴스 파라미터를 NO(0)로 해제.
        /// 실제로 값을 바꿨으면 true, 파라미터가 없거나 이미 0이면 false.
        /// </summary>
        public static bool UncheckYesNoParam(Element e, string paramName)
        {
            Parameter p = e.LookupParameter(paramName);
            if (p == null) return false;
            if (p.StorageType != StorageType.Integer) return false;
            if (p.IsReadOnly) return false;
            if (p.AsInteger() == 0) return false; // 이미 해제됨

            p.Set(0);
            return true;
        }
    }
}
