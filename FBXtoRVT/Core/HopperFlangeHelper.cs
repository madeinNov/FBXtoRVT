using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "HOPPER&amp;플랜지" 기능의 핵심 로직.
    ///
    /// 처리 흐름 (HOPPER 1대 기준)
    ///  1) 현재 뷰에서 패밀리명에 "HOPPER" 가 포함된 객체와 그 바운딩 박스를 모은다.
    ///  2) 그 박스 안에 중심점이 들어가는 FLANGE(패밀리명에 "FLANGE" 포함)가
    ///     "딱 1개" 일 때만 그 플랜지를 연결 대상으로 인식한다.
    ///  3) 플랜지의 커넥터 중 HOPPER 에 가까운 쪽이 Primary 커넥터인지 조사하고,
    ///     플랜지 종류에 따라 아래 파라미터를 해제한다.
    ///
    ///       가까운 커넥터가 Primary 인 경우
    ///         NW FLANGE    : "FLANGE 하" 해제
    ///         DC FLANGE    : "FLANGE 상" 해제
    ///         BLIND FLANGE : 파라미터 수정 없음
    ///
    ///       가까운 커넥터가 Primary 가 아닌 경우
    ///         NW FLANGE    : "FLANGE 상" 해제
    ///         DC FLANGE    : "FLANGE 하" 해제
    ///         BLIND FLANGE : 파라미터 수정 없음
    ///
    ///  4) HOPPER 의 Primary 가 아닌 커넥터를 위에서 조사한 플랜지 커넥터에 연결한다.
    ///     (HOPPER 가 이동·회전한다)
    /// </summary>
    public static class HopperFlangeHelper
    {
        // 대상 패밀리명 키워드
        private const string HopperFamilyKeyword = "HOPPER";
        private const string FlangeFamilyKeyword = "FLANGE";

        // 플랜지 종류 구분용 패밀리명 키워드
        private const string NwFlangeKeyword = "NW";
        private const string DcFlangeKeyword = "DC";
        private const string BlindFlangeKeyword = "BLIND";

        // 해제 대상 YES/NO 인스턴스 파라미터 이름
        private const string ParamFlangeLower = "FLANGE 하";
        private const string ParamFlangeUpper = "FLANGE 상";

        /// <summary>플랜지 종류.</summary>
        private enum FlangeKind
        {
            Unknown, // NW / DC / BLIND 중 어디에도 해당하지 않음
            Nw,
            Dc,
            Blind
        }

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int HopperCount;          // 찾은 HOPPER 수
            public int TargetFlangeCount;    // 연결 대상으로 인식한 플랜지 수(박스 안에 딱 1개)
            public int SkippedCount;         // 박스 안 플랜지가 0개이거나 2개 이상이라 건너뛴 HOPPER 수
            public int ParamUncheckedCount;  // 실제로 해제한 파라미터 수
            public int ConnectedCount;       // 연결 성공 수
            public int FailedCount;          // 연결 실패 수
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            // 처리 도중 HOPPER 가 이동하므로, 대상은 Id 목록으로 먼저 확정해 둔다.
            var hopperIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, HopperFamilyKeyword))
            {
                hopperIds.Add(fi.Id);
            }
            result.HopperCount = hopperIds.Count;

            var flangeIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FlangeFamilyKeyword))
            {
                flangeIds.Add(fi.Id);
            }

            // 한 플랜지를 여러 HOPPER 가 나눠 쓰지 않도록 기록
            var usedFlangeIds = new HashSet<ElementId>();

            LogUtils.Log($"===== HOPPER&플랜지 실행 시작. HOPPER {hopperIds.Count}개, FLANGE 후보 {flangeIds.Count}개 =====");

            foreach (ElementId hopperId in hopperIds)
            {
                ProcessOneHopper(doc, hopperId, flangeIds, usedFlangeIds, result);
            }

            LogUtils.Log($"===== HOPPER&플랜지 실행 종료. 대상={result.TargetFlangeCount} 건너뜀={result.SkippedCount} 파라미터해제={result.ParamUncheckedCount} 연결성공={result.ConnectedCount} 연결실패={result.FailedCount} =====");

            return result;
        }

        /// <summary>
        /// HOPPER 1대 처리. 조건이 맞지 않으면 아무것도 하지 않는다.
        /// </summary>
        private static void ProcessOneHopper(Document doc, ElementId hopperId,
            List<ElementId> flangeIds, HashSet<ElementId> usedFlangeIds, RunResult result)
        {
            var hopper = doc.GetElement(hopperId) as FamilyInstance;
            if (hopper == null) return;

            ElementUtils.WorldBox hopperBox = ElementUtils.GetWorldBox(hopper);
            if (hopperBox == null)
            {
                LogUtils.Log($"HOPPER(Id={hopperId}) 바운딩박스 없음, 건너뜀.");
                return;
            }

            XYZ hopperCenter = hopperBox.Center;
            LogUtils.Log($"HOPPER(Id={hopperId}) box Min={FormatXyz(hopperBox.Min)} Max={FormatXyz(hopperBox.Max)}");

            // 1) 박스 안에 중심점이 들어가는 플랜지를 센다. 딱 1개일 때만 대상.
            ElementId targetFlangeId = null;
            int insideCount = 0;

            foreach (ElementId id in flangeIds)
            {
                if (id == hopperId) continue;                 // HOPPER 자신은 제외
                if (usedFlangeIds.Contains(id)) continue;

                Element flange = doc.GetElement(id);
                if (flange == null) continue;

                XYZ center = ElementUtils.GetCenter(flange);
                bool inside = center != null && hopperBox.Contains(center);
                LogUtils.Log($"  후보 FLANGE(Id={id}, Family={GetFamilyName(flange)}) center={FormatXyz(center)} inside={inside}");
                if (!inside) continue;

                insideCount++;
                targetFlangeId = id;
            }

            if (insideCount != 1)
            {
                LogUtils.Log($"HOPPER(Id={hopperId}) 박스 안 FLANGE 개수={insideCount} (1개가 아님) -> 건너뜀.");
                result.SkippedCount++;
                return;
            }

            result.TargetFlangeCount++;

            // 2) 플랜지 커넥터 중 HOPPER 에 가까운 쪽을 고르고, Primary 인지 조사
            Element targetFlange = doc.GetElement(targetFlangeId);

            List<Connector> targetEndConns = ElementUtils.GetEndConnectors(targetFlange);
            LogUtils.Log($"HOPPER(Id={hopperId}) 대상 FLANGE(Id={targetFlangeId}, Family={GetFamilyName(targetFlange)}) End 커넥터 {targetEndConns.Count}개: " +
                string.Join(", ", targetEndConns.ConvertAll(c => $"[Id={c.Id} Origin={FormatXyz(c.Origin)} Primary={ElementUtils.IsPrimaryConnector(c)} Connected={c.IsConnected}]")));

            Connector nearConn = FindNearestEndConnector(targetFlange, hopperCenter);
            if (nearConn == null)
            {
                LogUtils.Log($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId})에서 End 커넥터를 찾지 못함 -> 실패.");
                result.FailedCount++;
                return;
            }

            int flangeConnId = nearConn.Id;
            bool isPrimary = ElementUtils.IsPrimaryConnector(nearConn);

            // 3) 플랜지 종류 + Primary 여부에 따라 파라미터 해제
            FlangeKind kind = GetFlangeKind(targetFlange);
            string paramToUncheck = GetParamToUncheck(kind, isPrimary);
            LogUtils.Log($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId}) kind={kind} nearConnId={flangeConnId} isPrimary={isPrimary} paramToUncheck={paramToUncheck ?? "(없음)"}");

            if (paramToUncheck != null && ElementUtils.UncheckYesNoParam(targetFlange, paramToUncheck))
            {
                result.ParamUncheckedCount++;
                doc.Regenerate(); // 형상이 바뀌므로 커넥터를 다시 조회해야 한다
            }

            // 4) 파라미터 변경 뒤 커넥터를 다시 조회 (Id 가 사라졌으면 다시 가까운 것으로 대체)
            Connector flangeConn = ElementUtils.ResolveConnector(doc, targetFlangeId, flangeConnId);
            if (flangeConn == null)
                flangeConn = FindNearestEndConnector(doc.GetElement(targetFlangeId), hopperCenter);

            if (flangeConn == null || flangeConn.IsConnected)
            {
                LogUtils.Log($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId}) 파라미터 변경 후 커넥터 재조회 실패. flangeConn={(flangeConn == null ? "null" : $"Id={flangeConn.Id} Connected={flangeConn.IsConnected}")}");
                result.FailedCount++;
                return;
            }

            // 5) HOPPER 의 Primary 가 아닌 열린 커넥터를 고른다. (여러 개면 플랜지에 가까운 것)
            Connector hopperConn = FindNonPrimaryOpenConnector(doc.GetElement(hopperId), flangeConn.Origin);
            if (hopperConn == null)
            {
                List<Connector> hopperOpenConns = ElementUtils.GetOpenEndConnectors(doc.GetElement(hopperId));
                LogUtils.Log($"HOPPER(Id={hopperId}) Non-Primary 열린 커넥터 없음. 열린 커넥터 {hopperOpenConns.Count}개: " +
                    string.Join(", ", hopperOpenConns.ConvertAll(c => $"[Id={c.Id} Origin={FormatXyz(c.Origin)} Primary={ElementUtils.IsPrimaryConnector(c)}]")));
                result.FailedCount++;
                return;
            }

            // 6) 플랜지 커넥터를 기준(Main)으로 두고, HOPPER(Sub)를 이동·회전시켜 연결
            try
            {
                ConnectorHelper.AlignAndConnect(doc, flangeConn, hopperConn, hopperId);
                doc.Regenerate();

                usedFlangeIds.Add(targetFlangeId);
                result.ConnectedCount++;
                LogUtils.Log($"HOPPER(Id={hopperId}) <-> FLANGE(Id={targetFlangeId}) 연결 성공.");
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"HOPPER(Id={hopperId}) <-> FLANGE(Id={targetFlangeId}) 연결 실패.");
                result.FailedCount++;
            }
        }

        private static string GetFamilyName(Element e)
        {
            var fi = e as FamilyInstance;
            return fi?.Symbol?.Family?.Name ?? "(알수없음)";
        }

        private static string FormatXyz(XYZ p)
        {
            return p == null ? "null" : $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }

        /// <summary>
        /// 패밀리명으로 플랜지 종류를 판별한다.
        /// (BLIND 를 먼저 보고, 그다음 NW / DC 순으로 검사)
        /// </summary>
        private static FlangeKind GetFlangeKind(Element flange)
        {
            if (ElementUtils.FamilyNameContains(flange, BlindFlangeKeyword)) return FlangeKind.Blind;
            if (ElementUtils.FamilyNameContains(flange, NwFlangeKeyword)) return FlangeKind.Nw;
            if (ElementUtils.FamilyNameContains(flange, DcFlangeKeyword)) return FlangeKind.Dc;

            return FlangeKind.Unknown;
        }

        /// <summary>
        /// 플랜지 종류와 "가까운 커넥터가 Primary 인지" 에 따라 해제할 파라미터 이름을 반환.
        /// 해제할 것이 없으면 null. (BLIND / 종류 불명은 파라미터를 건드리지 않는다)
        /// </summary>
        private static string GetParamToUncheck(FlangeKind kind, bool isPrimary)
        {
            if (kind == FlangeKind.Nw)
                return isPrimary ? ParamFlangeLower : ParamFlangeUpper;

            if (kind == FlangeKind.Dc)
                return isPrimary ? ParamFlangeUpper : ParamFlangeLower;

            return null;
        }

        /// <summary>
        /// 객체의 End 커넥터 중 기준점에 가장 가까운 것을 반환. 없으면 null.
        /// (거리가 같으면 먼저 만난 것 = 둘 중 아무거나)
        /// </summary>
        private static Connector FindNearestEndConnector(Element e, XYZ target)
        {
            Connector best = null;
            double bestDist = double.MaxValue;

            foreach (Connector c in ElementUtils.GetEndConnectors(e))
            {
                double dist = c.Origin.DistanceTo(target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// 객체의 열린 End 커넥터 중 Primary 가 아닌 것을 반환. (여러 개면 기준점에 가까운 것)
        /// 없으면 null.
        /// </summary>
        private static Connector FindNonPrimaryOpenConnector(Element e, XYZ target)
        {
            Connector best = null;
            double bestDist = double.MaxValue;

            foreach (Connector c in ElementUtils.GetOpenEndConnectors(e))
            {
                if (ElementUtils.IsPrimaryConnector(c)) continue;

                double dist = c.Origin.DistanceTo(target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }
    }
}
