using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "HOPPER&amp;플랜지" 기능의 핵심 로직.
    ///
    /// 처리 흐름 (HOPPER 1대 기준)
    ///  1) 현재 뷰에서 패밀리명에 "HOPPER" 가 포함된 객체의 바운딩 박스를 구하고,
    ///     모든 방향으로 50mm 키운다.
    ///  2) 그 박스 안에 "중심점 또는 커넥터점" 이 들어가는 FLANGE(패밀리명에 "FLANGE" 포함)가
    ///     "딱 1개" 일 때만 그 플랜지를 연결 대상으로 인식한다.
    ///  2-1) HOPPER 의 모든 커넥터 굵기(ND)가 서로 같을 때에 한해,
    ///     플랜지의 "ND1" 값을 HOPPER 의 "ND1" 에 복사한다.
    ///     (HOPPER 커넥터가 50A / 75A 처럼 서로 다르면 복사하지 않는다)
    ///  3) 플랜지의 커넥터 중 HOPPER 에 가까운 쪽이 Primary 커넥터인지 조사하고,
    ///     <b>지금 붙이는 커넥터 쪽 플랜지를 해제한다.</b>
    ///     그 커넥터가 상인지 하인지는 패밀리마다 다르므로 <see cref="FlangeSideTable"/> 의 표를 본다.
    ///     (표에 없는 패밀리와 BLIND FLANGE 는 파라미터를 건드리지 않는다)
    ///
    ///  4) HOPPER 의 Primary 가 아닌 커넥터를 위에서 조사한 플랜지 커넥터에 연결한다.
    ///     (HOPPER 가 이동·회전한다)
    /// </summary>
    public static class HopperFlangeHelper
    {
        // HOPPER 바운딩 박스 확장량(mm). 모든 방향(X/Y/Z 앞뒤)으로 이만큼 키운다.
        private const double HopperBoxExpandMm = 50.0;

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int HopperCount;          // 찾은 HOPPER 수
            public int TargetFlangeCount;    // 연결 대상으로 인식한 플랜지 수(박스 안에 딱 1개)
            public int SkippedCount;         // 박스 안 플랜지가 0개이거나 2개 이상이라 건너뛴 HOPPER 수
            public int ParamUncheckedCount;  // 실제로 해제한 파라미터 수
            public int Nd1CopiedCount;       // 플랜지 ND1 을 HOPPER 에 복사한 수
            public int Nd1SkippedMixedCount; // HOPPER 커넥터 ND 가 서로 달라 복사하지 않은 수
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
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Hopper))
            {
                hopperIds.Add(fi.Id);
            }
            result.HopperCount = hopperIds.Count;

            var flangeIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Flange))
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

            LogUtils.Log($"===== HOPPER&플랜지 실행 종료. 대상={result.TargetFlangeCount} 건너뜀={result.SkippedCount} 파라미터해제={result.ParamUncheckedCount} ND1복사={result.Nd1CopiedCount} ND1미적용(커넥터ND불일치)={result.Nd1SkippedMixedCount} 연결성공={result.ConnectedCount} 연결실패={result.FailedCount} =====");

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
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) 바운딩박스 없음, 건너뜀.");
                return;
            }

            // 박스를 모든 방향으로 50mm 키운다. (플랜지가 살짝 벗어나 있어도 잡히도록)
            hopperBox = hopperBox.ExpandAll(ElementUtils.MmToFeet(HopperBoxExpandMm));

            XYZ hopperCenter = hopperBox.Center;

            if (LogUtils.DetailEnabled)
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) box(+{HopperBoxExpandMm}mm) " +
                    $"Min={LogUtils.FormatXyz(hopperBox.Min)} Max={LogUtils.FormatXyz(hopperBox.Max)}");

            // 1) 박스 안에 "중심점 또는 커넥터점" 이 들어가는 플랜지를 센다. 딱 1개일 때만 대상.
            ElementId targetFlangeId = null;
            int insideCount = 0;

            foreach (ElementId id in flangeIds)
            {
                if (id == hopperId) continue;                 // HOPPER 자신은 제외
                if (usedFlangeIds.Contains(id)) continue;

                Element flange = doc.GetElement(id);
                if (flange == null) continue;

                XYZ center = ElementUtils.GetCenter(flange);
                bool centerInside = center != null && hopperBox.Contains(center);
                bool connInside = IsAnyConnectorInside(flange, hopperBox);

                bool inside = centerInside || connInside;

                // 반복문 안이라 호출 자체를 if 로 막는다. (문자열 만드는 비용까지 아끼기 위해)
                if (LogUtils.DetailEnabled)
                    LogUtils.LogDetail($"  후보 FLANGE(Id={id}, Family={ElementUtils.GetFamilyName(flange)}) " +
                        $"center={LogUtils.FormatXyz(center)} 중심점inside={centerInside} 커넥터점inside={connInside}");

                if (!inside) continue;

                insideCount++;
                targetFlangeId = id;
            }

            if (insideCount != 1)
            {
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) 박스 안 FLANGE 개수={insideCount} (1개가 아님) -> 건너뜀.");
                result.SkippedCount++;
                return;
            }

            result.TargetFlangeCount++;

            // 1-1) HOPPER 의 모든 커넥터 굵기(ND)가 같을 때만, 플랜지의 ND1 을 HOPPER 의 ND1 에 복사
            CopyNd1ToHopper(doc, hopperId, targetFlangeId, result);

            // 2) 플랜지 커넥터 중 HOPPER 에 가까운 쪽을 고르고, Primary 인지 조사
            Element targetFlange = doc.GetElement(targetFlangeId);

            if (LogUtils.DetailEnabled)
            {
                List<Connector> targetEndConns = ElementUtils.GetEndConnectors(targetFlange);
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) 대상 FLANGE(Id={targetFlangeId}, Family={ElementUtils.GetFamilyName(targetFlange)}) " +
                    $"End 커넥터 {targetEndConns.Count}개: " +
                    string.Join(", ", targetEndConns.ConvertAll(c =>
                        $"[Id={c.Id} Origin={LogUtils.FormatXyz(c.Origin)} Primary={ElementUtils.IsPrimaryConnector(c)} Connected={c.IsConnected}]")));
            }

            Connector nearConn = ElementUtils.FindNearestEndConnector(targetFlange, hopperCenter);
            if (nearConn == null)
            {
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId})에서 End 커넥터를 찾지 못함 -> 실패.");
                result.FailedCount++;
                return;
            }

            int flangeConnId = nearConn.Id;
            bool isPrimary = ElementUtils.IsPrimaryConnector(nearConn);

            // 3) 지금 붙이는 커넥터 쪽 플랜지를 해제한다. (상/하 판단은 패밀리별 표가 한다)
            string paramToUncheck = FlangeSideTable.GetParamToUncheck(targetFlange, isPrimary);

            if (LogUtils.DetailEnabled)
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId}) " +
                    $"Primary쪽={FlangeSideTable.GetPrimarySide(targetFlange)} " +
                    $"nearConnId={flangeConnId} isPrimary={isPrimary} paramToUncheck={paramToUncheck ?? "(없음)"}");

            if (paramToUncheck != null && ElementUtils.UncheckYesNoParam(targetFlange, paramToUncheck))
            {
                result.ParamUncheckedCount++;
                doc.Regenerate(); // 형상이 바뀌므로 커넥터를 다시 조회해야 한다
            }

            // 4) 파라미터 변경 뒤 커넥터를 다시 조회 (Id 가 사라졌으면 다시 가까운 것으로 대체)
            Connector flangeConn = ElementUtils.ResolveConnector(doc, targetFlangeId, flangeConnId);
            if (flangeConn == null)
                flangeConn = ElementUtils.FindNearestEndConnector(doc.GetElement(targetFlangeId), hopperCenter);

            if (flangeConn == null || flangeConn.IsConnected)
            {
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) FLANGE(Id={targetFlangeId}) 파라미터 변경 후 커넥터 재조회 실패. flangeConn={(flangeConn == null ? "null" : $"Id={flangeConn.Id} Connected={flangeConn.IsConnected}")}");
                result.FailedCount++;
                return;
            }

            // 5) HOPPER 의 Primary 가 아닌 열린 커넥터를 고른다. (여러 개면 플랜지에 가까운 것)
            Connector hopperConn = FindNonPrimaryOpenConnector(doc.GetElement(hopperId), flangeConn.Origin);
            if (hopperConn == null)
            {
                if (LogUtils.DetailEnabled)
                {
                    List<Connector> hopperOpenConns = ElementUtils.GetOpenEndConnectors(doc.GetElement(hopperId));
                    LogUtils.LogDetail($"HOPPER(Id={hopperId}) Non-Primary 열린 커넥터 없음. 열린 커넥터 {hopperOpenConns.Count}개: " +
                        string.Join(", ", hopperOpenConns.ConvertAll(c =>
                            $"[Id={c.Id} Origin={LogUtils.FormatXyz(c.Origin)} Primary={ElementUtils.IsPrimaryConnector(c)}]")));
                }

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
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) <-> FLANGE(Id={targetFlangeId}) 연결 성공.");
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"HOPPER(Id={hopperId}) <-> FLANGE(Id={targetFlangeId}) 연결 실패.");
                result.FailedCount++;
            }
        }

        /// <summary>
        /// 객체의 End 커넥터 원점 중 하나라도 박스 안에 들어가면 true.
        /// </summary>
        private static bool IsAnyConnectorInside(Element e, ElementUtils.WorldBox box)
        {
            foreach (Connector c in ElementUtils.GetEndConnectors(e))
            {
                if (box.Contains(c.Origin)) return true;
            }

            return false;
        }

        /// <summary>
        /// 플랜지의 "ND1" 값을 HOPPER 의 "ND1" 에 복사한다.
        ///
        /// 단, HOPPER 의 모든 커넥터 굵기(ND)가 서로 같을 때만 복사한다.
        /// HOPPER 가 50A / 75A 처럼 서로 다른 커넥터를 갖고 있으면, ND1 하나만 바꾸면
        /// 나머지 커넥터까지 잘못 바뀔 수 있으므로 아무것도 하지 않는다.
        ///
        /// 값을 바꾸면 형상이 달라지므로 곧바로 Regenerate 한다.
        /// (이후 HOPPER 커넥터는 반드시 다시 조회해야 한다)
        /// </summary>
        private static void CopyNd1ToHopper(Document doc, ElementId hopperId, ElementId flangeId, RunResult result)
        {
            Element hopper = doc.GetElement(hopperId);
            Element flange = doc.GetElement(flangeId);
            if (hopper == null || flange == null) return;

            // HOPPER 의 모든 커넥터 굵기를 비교 (연결 여부와 상관없이 전체를 본다)
            List<Connector> hopperConns = ElementUtils.GetEndConnectors(hopper);
            if (hopperConns.Count == 0)
            {
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) 커넥터가 없어 ND1 복사를 건너뜀.");
                return;
            }

            string firstSizeKey = ElementUtils.GetConnectorSizeKey(hopperConns[0]);
            foreach (Connector c in hopperConns)
            {
                if (ElementUtils.GetConnectorSizeKey(c) == firstSizeKey) continue;

                if (LogUtils.DetailEnabled)
                    LogUtils.LogDetail($"HOPPER(Id={hopperId}) 커넥터 ND 가 서로 다름 -> ND1 복사 안 함. " +
                        string.Join(", ", hopperConns.ConvertAll(x => $"[Id={x.Id} size={ElementUtils.GetConnectorSizeKey(x)}]")));

                result.Nd1SkippedMixedCount++;
                return;
            }

            // 모든 커넥터 ND 가 같으므로 복사
            if (ElementUtils.CopyParamValue(flange, hopper, ParamNames.Nd1))
            {
                result.Nd1CopiedCount++;
                doc.Regenerate(); // ND1 이 바뀌면 형상/커넥터가 바뀐다
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) <- FLANGE(Id={flangeId}) 의 {ParamNames.Nd1} 값 복사 완료.");
            }
            else
            {
                LogUtils.LogDetail($"HOPPER(Id={hopperId}) {ParamNames.Nd1} 복사 안 됨(파라미터 없음 / 읽기전용 / 이미 같은 값).");
            }
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
