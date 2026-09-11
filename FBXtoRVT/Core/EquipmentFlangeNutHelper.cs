using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "플랜지/NUT/VCR" 기능의 핵심 로직. (예전 이름: 장비&amp;플랜지/NUT)
    /// ScrubberFlangeHelper 와 동일한 규칙이되, 대상이 'SCRUBBER' 패밀리가 아니라
    /// Mechanical Equipment 카테고리 전체이고, 장비 바운딩 박스를 모든 방향으로 20mm 확장해서 쓴다.
    ///
    /// 처리 흐름 (장비 1대 기준)
    ///  1) Mechanical Equipment 카테고리 객체의 바운딩 박스(모든 방향 +20mm)와 열린 커넥터를 모은다.
    ///  2) 그 박스 안에 중심점이 들어가는 부품(FLANGE / NUT / VCR)을 모으고, 부품별 바운딩 박스를 구한다.
    ///  3) 부품 바운딩 박스 안에 장비의 열린 커넥터가 "정확히 1개" 들어있으면,
    ///     그 커넥터를 그 부품의 대상 커넥터로 인식한다.
    ///  4) 부품 종류와 열린 커넥터 개수에 따라 파라미터를 해제하고, 부품을 이동/회전시켜 연결한다.
    ///
    ///  FLANGE
    ///   - 열린 커넥터 2개      : "FLANGE 하" 해제 후 Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개(Primary)      : 위와 동일
    ///   - 열린 커넥터 1개(Primary 아님)  : "FLANGE 상" 해제 후 그 열린 커넥터를 대상 커넥터에 연결
    ///
    ///  FLANGE 중 이름(패밀리명 또는 타입명)에 "BELLOWS" 가 들어간 것은 상/하가 반대다.
    ///   - Primary      : "FLANGE 상" 해제
    ///   - Primary 아님  : "FLANGE 하" 해제
    ///
    ///  NUT (파라미터 해제 없음)
    ///   - 열린 커넥터 2개 : Primary 커넥터를 대상 커넥터에 연결
    ///   - 열린 커넥터 1개 : 그 열린 커넥터를 대상 커넥터에 연결
    ///
    ///  VCR (파라미터 해제 없음. 예: ASSEMBLY_VCR_STS316L EP)
    ///   - 열린 커넥터 개수와 상관없이 <b>항상 Primary 커넥터</b>를 장비 커넥터에 연결한다.
    ///     (= Primary 가 장비쪽에 붙어야 한다)
    ///   - Primary 가 이미 다른 객체에 붙어 있으면: 그 상대를 떼어 낸다 → Primary 를 장비에 붙인다
    ///     → 반대쪽 커넥터에 아까 떼어 낸 상대를 다시 붙인다. (상대가 부품이면 상대를 이동·회전,
    ///     배관이면 배관 끝점을 커넥터 자리로 옮겨서 연결)
    /// </summary>
    public static class EquipmentFlangeNutHelper
    {
        /// <summary>
        /// 부품 종류. 종류마다 어느 커넥터를 쓰고 어떤 파라미터를 해제할지가 다르다.
        /// </summary>
        public enum PartKind
        {
            Flange,   // FLANGE: 파라미터 해제 + Primary/열린 커넥터
            Nut,      // NUT   : 파라미터 해제 없음 + Primary/열린 커넥터
            Vcr       // VCR   : 파라미터 해제 없음 + 항상 Primary
        }

        // 대상 패밀리명 키워드 (패밀리명에 이 글자가 들어가면 그 종류로 본다)
        private const string FlangeFamilyKeyword = "FLANGE";
        private const string NutFamilyKeyword = "NUT";
        private const string VcrFamilyKeyword = "VCR";

        // 상/하 해제 규칙이 반대가 되는 부품 이름 키워드
        private const string BellowsKeyword = "BELLOWS";

        // 해제 대상 YES/NO 인스턴스 파라미터 이름
        private const string ParamFlangeLower = "FLANGE 하";
        private const string ParamFlangeUpper = "FLANGE 상";

        // 장비 바운딩 박스 확장량(mm). 모든 방향(X/Y/Z 앞뒤)으로 이만큼 키운다.
        private const double EquipBoxExpandMm = 20.0;

        /// <summary>
        /// 부품 종류 하나의 집계. (대상 인식 / 연결 성공 / 실패)
        /// </summary>
        public class PartCount
        {
            public int TargetCount;          // 대상 커넥터를 인식한 부품 수
            public int ConnectedCount;       // 연결 성공 수
            public int FailedCount;          // 연결 실패 수
            public int ReattachedCount;      // (VCR) 떼어 낸 상대를 반대쪽에 다시 붙인 수
            public int ReattachFailedCount;  // (VCR) 다시 붙이지 못한 수
        }

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int EquipmentCount;                       // 찾은 장비 수
            public int ParamUncheckedCount;                  // 실제로 해제한 파라미터 수
            public PartCount Flange = new PartCount();       // FLANGE 집계
            public PartCount Nut = new PartCount();          // NUT 집계
            public PartCount Vcr = new PartCount();          // VCR 집계

            /// <summary>부품 종류에 맞는 집계 객체를 돌려준다.</summary>
            public PartCount Of(PartKind kind)
            {
                switch (kind)
                {
                    case PartKind.Flange: return Flange;
                    case PartKind.Nut: return Nut;
                    default: return Vcr;
                }
            }
        }

        /// <summary>
        /// 장비 커넥터를 "객체 Id + 커넥터 Id" 로 기억해 두는 참조.
        /// 부품을 붙이는 도중 문서가 바뀌므로, 쓰기 직전에 다시 조회한다.
        /// </summary>
        private class ConnRef
        {
            public ElementId OwnerId;
            public int ConnectorId;
            public XYZ Origin;

            /// <summary>중복 사용 방지를 위한 키</summary>
            public string Key
            {
                get { return OwnerId.Value + ":" + ConnectorId; }
            }
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            double expandFeet = ElementUtils.MmToFeet(EquipBoxExpandMm);

            // 처리 도중 부품이 이동하므로, 장비는 Id 목록으로 먼저 확정해 둔다.
            var equipIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstancesByCategory(doc, view, BuiltInCategory.OST_MechanicalEquipment))
            {
                equipIds.Add(fi.Id);
            }
            result.EquipmentCount = equipIds.Count;

            // 이미 부품이 붙은 장비 커넥터는 다시 쓰지 않도록 기록
            var usedEquipConnKeys = new HashSet<string>();

            foreach (ElementId equipId in equipIds)
            {
                var equip = doc.GetElement(equipId) as FamilyInstance;
                if (equip == null) continue;

                ElementUtils.WorldBox equipBox = ElementUtils.GetWorldBox(equip);
                if (equipBox == null) continue;

                equipBox = equipBox.ExpandAll(expandFeet);

                // 장비의 열린 커넥터 정보 수집
                var equipConns = new List<ConnRef>();
                foreach (Connector c in ElementUtils.GetOpenEndConnectors(equip))
                {
                    equipConns.Add(new ConnRef
                    {
                        OwnerId = equipId,
                        ConnectorId = c.Id,
                        Origin = c.Origin
                    });
                }

                if (equipConns.Count == 0) continue;

                // FLANGE 처리 → NUT 처리 → VCR 처리
                ProcessParts(doc, view, equipId, equipBox, equipConns, usedEquipConnKeys,
                    FlangeFamilyKeyword, PartKind.Flange, result);

                ProcessParts(doc, view, equipId, equipBox, equipConns, usedEquipConnKeys,
                    NutFamilyKeyword, PartKind.Nut, result);

                ProcessParts(doc, view, equipId, equipBox, equipConns, usedEquipConnKeys,
                    VcrFamilyKeyword, PartKind.Vcr, result);
            }

            return result;
        }

        /// <summary>
        /// 장비 박스 안의 부품(FLANGE / NUT / VCR)을 찾아 대상 커넥터에 연결한다.
        /// </summary>
        /// <param name="familyKeyword">패밀리명에서 찾을 글자</param>
        /// <param name="kind">부품 종류 (종류별 연결 규칙이 다르다)</param>
        private static void ProcessParts(Document doc, View view, ElementId equipId,
            ElementUtils.WorldBox equipBox, List<ConnRef> equipConns, HashSet<string> usedEquipConnKeys,
            string familyKeyword, PartKind kind, RunResult result)
        {
            // 처리 도중 부품이 이동하므로 Id 목록으로 먼저 확정
            var partIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, familyKeyword))
            {
                if (fi.Id == equipId) continue; // 장비 자신은 제외

                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(fi);
                if (box == null) continue;

                // 부품 중심점이 장비 박스 안에 있어야 대상
                if (!equipBox.Contains(box.Center)) continue;

                partIds.Add(fi.Id);
            }

            foreach (ElementId partId in partIds)
            {
                ProcessOnePart(doc, partId, equipConns, usedEquipConnKeys, kind, result);
            }
        }

        /// <summary>
        /// 부품 1개 처리. 대상 커넥터를 인식하지 못하면 아무것도 하지 않는다.
        /// </summary>
        private static void ProcessOnePart(Document doc, ElementId partId, List<ConnRef> equipConns,
            HashSet<string> usedEquipConnKeys, PartKind kind, RunResult result)
        {
            var part = doc.GetElement(partId) as FamilyInstance;
            if (part == null) return;

            ElementUtils.WorldBox partBox = ElementUtils.GetWorldBox(part);
            if (partBox == null) return;

            PartCount count = result.Of(kind);

            // 1) 부품 박스 안에 들어있는 (아직 쓰지 않은) 장비 열린 커넥터 찾기
            ConnRef target = null;
            int insideCount = 0;

            foreach (ConnRef c in equipConns)
            {
                if (usedEquipConnKeys.Contains(c.Key)) continue;
                if (!partBox.Contains(c.Origin)) continue;

                insideCount++;
                target = c;
            }

            // 정확히 1개일 때만 대상 커넥터로 인식
            if (insideCount != 1) return;

            // 2) 부품 종류와 열린 커넥터 개수에 따라 처리 방법 결정
            List<Connector> openConns = ElementUtils.GetOpenEndConnectors(part);

            string paramToUncheck = null; // 해제할 파라미터 (NUT / VCR 은 없음)
            bool usePrimary;              // Primary 커넥터를 쓸지 여부
            int chosenConnectorId = -1;   // Primary 를 쓰지 않을 때 사용할 커넥터 Id

            if (kind == PartKind.Vcr)
            {
                // VCR: 열린 커넥터 개수와 상관없이 항상 Primary 를 장비쪽에 붙인다.
                if (openConns.Count == 0) return;
                usePrimary = true;
            }
            else if (openConns.Count == 2)
            {
                // 열린 커넥터 2개 → Primary 커넥터 사용
                usePrimary = true;
                if (kind == PartKind.Flange)
                    paramToUncheck = GetFlangeParamToUncheck(true, IsBellows(part));
            }
            else if (openConns.Count == 1)
            {
                Connector only = openConns[0];

                if (kind == PartKind.Nut)
                {
                    // NUT: 열린 커넥터를 그대로 사용
                    usePrimary = false;
                    chosenConnectorId = only.Id;
                }
                else if (ElementUtils.IsPrimaryConnector(only))
                {
                    // FLANGE + Primary → 2개일 때와 동일하게 처리
                    usePrimary = true;
                    paramToUncheck = GetFlangeParamToUncheck(true, IsBellows(part));
                }
                else
                {
                    // FLANGE + Primary 아님 → 그 커넥터를 사용
                    usePrimary = false;
                    chosenConnectorId = only.Id;
                    paramToUncheck = GetFlangeParamToUncheck(false, IsBellows(part));
                }
            }
            else
            {
                // 열린 커넥터가 0개이거나 3개 이상이면 대상이 아님
                return;
            }

            count.TargetCount++;

            // 3) 파라미터 해제 (형상이 바뀌므로 이후 커넥터는 다시 조회한다)
            if (paramToUncheck != null && ElementUtils.UncheckYesNoParam(part, paramToUncheck))
            {
                result.ParamUncheckedCount++;
                doc.Regenerate();
            }

            // 4) 실제로 연결할 커넥터를 다시 조회
            Connector subConn = usePrimary
                ? ElementUtils.GetPrimaryConnector(doc.GetElement(partId))
                : ElementUtils.ResolveConnector(doc, partId, chosenConnectorId);

            Connector targetConn = ElementUtils.ResolveConnector(doc, target.OwnerId, target.ConnectorId);

            if (subConn == null || targetConn == null || targetConn.IsConnected)
            {
                count.FailedCount++;
                return;
            }

            // 4-1) VCR 의 Primary 가 이미 다른 객체에 붙어 있으면:
            //      그 상대를 기억해 두고 일단 떼어 낸다. (장비에 붙인 뒤 반대쪽 커넥터에 다시 붙인다)
            ConnRef detachedPartner = null;

            if (subConn.IsConnected)
            {
                if (kind != PartKind.Vcr)
                {
                    // FLANGE / NUT 은 열린 커넥터만 쓰므로 여기 오면 붙일 수 없는 상태
                    count.FailedCount++;
                    return;
                }

                detachedPartner = FindConnectedPartner(subConn, partId);
                if (detachedPartner == null || !TryDisconnect(subConn, detachedPartner, doc))
                {
                    count.FailedCount++;
                    return;
                }

                // 떼어 낸 뒤 커넥터를 다시 조회한다.
                subConn = ElementUtils.GetPrimaryConnector(doc.GetElement(partId));
                targetConn = ElementUtils.ResolveConnector(doc, target.OwnerId, target.ConnectorId);

                if (subConn == null || subConn.IsConnected || targetConn == null)
                {
                    count.FailedCount++;
                    return;
                }
            }

            // 5) 장비 커넥터를 기준(Main)으로 두고, 부품(Sub)을 이동/회전시켜 연결
            try
            {
                ConnectorHelper.AlignAndConnect(doc, targetConn, subConn, partId);
                doc.Regenerate();

                usedEquipConnKeys.Add(target.Key);
                count.ConnectedCount++;
            }
            catch (Exception)
            {
                count.FailedCount++;
                return;
            }

            // 6) 아까 떼어 낸 상대가 있으면, 부품의 반대쪽 커넥터에 다시 붙인다.
            if (detachedPartner != null)
            {
                if (ReattachPartner(doc, partId, detachedPartner))
                    count.ReattachedCount++;
                else
                    count.ReattachFailedCount++;
            }
        }

        // ===== VCR: 떼었다가 반대쪽에 다시 붙이기 =====

        /// <summary>
        /// 커넥터에 물리적으로 연결돼 있는 상대 커넥터(자기 자신 제외)를 찾는다. 없으면 null.
        /// </summary>
        private static ConnRef FindConnectedPartner(Connector conn, ElementId selfId)
        {
            foreach (Connector other in conn.AllRefs)
            {
                if (other.ConnectorType != ConnectorType.End) continue;   // 논리적(System) 참조는 제외
                if (other.Owner == null || other.Owner.Id == selfId) continue;

                return new ConnRef
                {
                    OwnerId = other.Owner.Id,
                    ConnectorId = other.Id,
                    Origin = other.Origin
                };
            }

            return null;
        }

        /// <summary>
        /// 두 커넥터의 연결을 끊는다. 실패하면 false.
        /// </summary>
        private static bool TryDisconnect(Connector conn, ConnRef partner, Document doc)
        {
            Connector partnerConn = ElementUtils.ResolveConnector(doc, partner.OwnerId, partner.ConnectorId);
            if (partnerConn == null) return false;

            try
            {
                if (conn.IsConnectedTo(partnerConn)) conn.DisconnectFrom(partnerConn);
                doc.Regenerate();
                return true;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"VCR Primary 떼어 내기 실패. 상대 Id={partner.OwnerId}");
                return false;
            }
        }

        /// <summary>
        /// 아까 떼어 낸 상대를 부품의 반대쪽(Primary 가 아닌 열린) 커넥터에 다시 붙인다.
        ///
        /// 부품은 이미 장비에 붙어 있으므로 움직이지 않고, <b>상대 쪽을 움직여서</b> 맞춘다.
        ///  - 상대가 부품(FamilyInstance) : 상대를 이동·회전시켜 연결
        ///  - 상대가 배관(MEPCurve)       : 배관 끝점을 부품 커넥터 자리까지 늘리거나 줄여서 연결
        /// </summary>
        /// <returns>다시 붙였으면 true</returns>
        private static bool ReattachPartner(Document doc, ElementId partId, ConnRef partner)
        {
            try
            {
                Connector partnerConn = ElementUtils.ResolveConnector(doc, partner.OwnerId, partner.ConnectorId);
                if (partnerConn == null || partnerConn.IsConnected) return false;

                Element part = doc.GetElement(partId);
                Connector opposite = FindOppositeOpenConnector(part, partnerConn.Origin);
                if (opposite == null) return false;

                Element partnerOwner = doc.GetElement(partner.OwnerId);

                if (partnerOwner is FamilyInstance partnerFi)
                {
                    // 규칙 1: 복합 패밀리의 내부 부품이면 최상위 부모를 움직인다.
                    ElementId moveId = (partnerFi.SuperComponent != null) ? partnerFi.SuperComponent.Id : partnerFi.Id;

                    ConnectorHelper.AlignAndConnect(doc, opposite, partnerConn, moveId);
                    doc.Regenerate();
                    return true;
                }

                if (partnerOwner is MEPCurve partnerCurve)
                {
                    // 배관은 통째로 옮기지 않고, 부품 쪽 끝점만 커넥터 자리로 옮긴다.
                    if (!StretchCurveEndTo(doc, partnerCurve, partnerConn.Origin, opposite.Origin))
                        return false;

                    doc.Regenerate();

                    // 끝점을 옮겼으니 커넥터를 다시 조회해서 연결한다.
                    partnerConn = ElementUtils.ResolveConnector(doc, partner.OwnerId, partner.ConnectorId);
                    opposite = ElementUtils.ResolveConnector(doc, partId, opposite.Id);

                    if (partnerConn == null || opposite == null) return false;

                    if (!opposite.IsConnectedTo(partnerConn)) opposite.ConnectTo(partnerConn);
                    doc.Regenerate();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"VCR 반대쪽 다시 붙이기 실패. 부품 Id={partId} 상대 Id={partner.OwnerId}");
                return false;
            }
        }

        /// <summary>
        /// 부품의 Primary 가 아닌 열린 End 커넥터 중 <paramref name="hint"/> 에 가장 가까운 것.
        /// 없으면 null.
        /// </summary>
        private static Connector FindOppositeOpenConnector(Element part, XYZ hint)
        {
            Connector best = null;
            double bestDist = double.MaxValue;

            foreach (Connector c in ElementUtils.GetOpenEndConnectors(part))
            {
                if (ElementUtils.IsPrimaryConnector(c)) continue;

                double dist = c.Origin.DistanceTo(hint);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// 배관(MEPCurve)의 두 끝점 중 <paramref name="endToMove"/> 쪽 끝을 목표점으로 옮긴다.
        /// 반대쪽 끝은 그대로 둔다. 직선이 아니거나 너무 짧아지면 false.
        /// </summary>
        private static bool StretchCurveEndTo(Document doc, MEPCurve curve, XYZ endToMove, XYZ targetPoint)
        {
            var lc = curve.Location as LocationCurve;
            Line line = (lc != null) ? lc.Curve as Line : null;
            if (line == null) return false;

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);

            // 옮길 끝이 시작점 쪽인지 끝점 쪽인지 판정
            bool moveStart = start.DistanceTo(endToMove) <= end.DistanceTo(endToMove);
            XYZ movingEnd = moveStart ? start : end;
            XYZ fixedEnd = moveStart ? end : start;

            double minLength = doc.Application.ShortCurveTolerance;

            // 이미 목표점에 있으면 건드리지 않는다.
            if (movingEnd.DistanceTo(targetPoint) < minLength) return true;

            // 배관이 사라질 만큼 짧아지면 못 한다.
            if (fixedEnd.DistanceTo(targetPoint) < minLength) return false;

            lc.Curve = moveStart
                ? Line.CreateBound(targetPoint, fixedEnd)
                : Line.CreateBound(fixedEnd, targetPoint);

            return true;
        }

        /// <summary>이름에 BELLOWS 가 들어간 부품인지. (상/하 해제 규칙이 반대가 된다)</summary>
        private static bool IsBellows(FamilyInstance part)
        {
            return ElementUtils.NameContains(part, BellowsKeyword);
        }

        /// <summary>
        /// FLANGE 에서 해제할 파라미터 이름을 고른다.
        ///
        ///  보통 FLANGE : Primary 이면 "FLANGE 하", 아니면 "FLANGE 상"
        ///  BELLOWS     : 위와 반대로 Primary 이면 "FLANGE 상", 아니면 "FLANGE 하"
        /// </summary>
        /// <param name="isPrimary">Primary 커넥터를 쓰는 경우인지</param>
        /// <param name="isBellows">부품 이름에 BELLOWS 가 들어있는지</param>
        private static string GetFlangeParamToUncheck(bool isPrimary, bool isBellows)
        {
            if (isBellows)
                return isPrimary ? ParamFlangeUpper : ParamFlangeLower;

            return isPrimary ? ParamFlangeLower : ParamFlangeUpper;
        }
    }
}
