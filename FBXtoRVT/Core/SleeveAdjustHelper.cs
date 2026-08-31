using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "타공 슬리브 조정" 기능의 핵심 로직.
    ///
    /// 처리 흐름 (슬리브 1개 기준)
    ///  1) 패밀리명에 "타공 SLEEVE" 가 포함된 객체의 바운딩 박스를 구한다.
    ///  2) 그 박스의 상부를 100mm, 하부를 2000mm 키운 "탐색 박스" 를 만든다.
    ///  3) 탐색 박스 안에 중심점이 들어가는 "DC FLANGE" 객체를 모은다.
    ///  4) 그 중 탐색 박스 상부면 중심점 / 하부면 중심점에 가장 가까운 것을 각각 1개씩 삭제(최대 2개).
    ///  5) System Type 이 "Exhaust_Pumping" 인 배관 중, 끝점이 탐색 박스 안에 하나라도 있는
    ///     배관의 열린 커넥터를 모은다.
    ///  6) 슬리브의 Primary 커넥터를 "상부면 중심점에 가까운 배관 커넥터" 에 연결(슬리브가 이동/회전).
    ///     (슬리브 패밀리의 Primary 커넥터가 위쪽을 향하고 있으므로 상부가 기준이다)
    ///  7) 슬리브에 남아있는 열린 커넥터를 "하부면 중심점에 가까운 배관 커넥터" 에 연결.
    ///
    /// 2) 이후 나오는 "바운딩 박스" 는 모두 확장된 탐색 박스를 가리킨다.
    ///
    /// [속도] DC FLANGE 후보와 대상 배관은 <b>실행 시작 때 한 번만</b> 수집한다.
    /// 슬리브마다 FilteredElementCollector 를 다시 돌리면 큰 모델에서 매우 느려지기 때문이다.
    /// </summary>
    public static class SleeveAdjustHelper
    {
        // 대상 시스템 이름
        private const string TargetSystemTypeName = "Exhaust_Pumping";

        // 바운딩 박스 확장량(mm)
        private const double TopExpandMm = 100.0;
        private const double BottomExpandMm = 2000.0;

        /// <summary>
        /// 실행 결과 요약.
        /// </summary>
        public class RunResult
        {
            public int SleeveCount;           // 찾은 타공 SLEEVE 수
            public int DeletedFlangeCount;    // 삭제한 DC FLANGE 수
            public int TopConnectedCount;     // 상부(Primary) 연결 성공 수
            public int BottomConnectedCount;  // 하부(나머지 커넥터) 연결 성공 수
            public int FailedCount;           // 연결 실패 수
            public int NoPipeSleeveCount;     // 대상 배관 커넥터를 못 찾아 건너뛴 슬리브 수
        }

        /// <summary>
        /// 미리 모아 둔 DC FLANGE 후보. 이 기능에서 플랜지는 삭제만 되고 움직이지 않으므로,
        /// 중심점을 한 번만 계산해 두고 계속 쓴다.
        /// </summary>
        private class FlangeRef
        {
            public ElementId Id;
            public XYZ Center;
        }

        /// <summary>
        /// 미리 모아 둔 대상 배관. 이 기능에서 배관은 움직이지 않으므로 끝점을 한 번만 구해 둔다.
        /// (커넥터가 열렸는지 닫혔는지는 처리 도중 바뀌므로, 그때그때 다시 조회한다)
        /// </summary>
        private class PipeRef
        {
            public ElementId Id;
            public XYZ End0;
            public XYZ End1;
        }

        /// <summary>
        /// 메인 실행. (외부에서 Transaction 을 열고 호출해야 함)
        /// </summary>
        public static RunResult Run(Document doc, View view)
        {
            var result = new RunResult();

            // 처리 도중 삭제/이동이 일어나므로, 슬리브는 Id 목록으로 먼저 확정해 둔다.
            var sleeveIds = new List<ElementId>();
            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.Sleeve))
            {
                sleeveIds.Add(fi.Id);
            }
            result.SleeveCount = sleeveIds.Count;

            // 후보 목록은 여기서 한 번만 수집한다. (규칙: 반복문 안에서 다시 수집하지 않는다)
            List<FlangeRef> flanges = CollectFlanges(doc, view);
            List<PipeRef> targetPipes = CollectTargetPipes(doc, view);

            // 이미 지운 플랜지는 다음 슬리브에서 다시 후보로 보지 않는다.
            var deletedFlangeIds = new HashSet<ElementId>();

            LogUtils.Log($"===== 타공 슬리브 조정 실행 시작. 슬리브 {result.SleeveCount}개, " +
                $"DC FLANGE 후보 {flanges.Count}개, 대상 배관 {targetPipes.Count}개 =====");

            // 슬리브 하나씩 순서대로 처리
            foreach (ElementId sleeveId in sleeveIds)
            {
                ProcessOneSleeve(doc, sleeveId, flanges, deletedFlangeIds, targetPipes, result);
            }

            LogUtils.Log($"===== 타공 슬리브 조정 실행 종료. 삭제플랜지={result.DeletedFlangeCount} " +
                $"상부연결={result.TopConnectedCount} 하부연결={result.BottomConnectedCount} " +
                $"실패={result.FailedCount} 배관못찾음={result.NoPipeSleeveCount} =====");

            return result;
        }

        /// <summary>
        /// 슬리브 1개에 대한 전체 처리.
        /// </summary>
        private static void ProcessOneSleeve(Document doc, ElementId sleeveId,
            List<FlangeRef> flanges, HashSet<ElementId> deletedFlangeIds, List<PipeRef> targetPipes,
            RunResult result)
        {
            var sleeve = doc.GetElement(sleeveId) as FamilyInstance;
            if (sleeve == null) return;

            // 1~2) 슬리브 바운딩 박스를 위 100mm / 아래 2000mm 로 확장한 탐색 박스
            ElementUtils.WorldBox baseBox = ElementUtils.GetWorldBox(sleeve);
            if (baseBox == null) return;

            ElementUtils.WorldBox searchBox = baseBox.ExpandVertical(
                ElementUtils.MmToFeet(TopExpandMm),
                ElementUtils.MmToFeet(BottomExpandMm));

            XYZ topCenter = searchBox.TopFaceCenter;
            XYZ bottomCenter = searchBox.BottomFaceCenter;

            // 3~4) 탐색 박스 안의 DC FLANGE 중 상/하부면 중심점에 가장 가까운 것 삭제
            DeleteNearestFlanges(doc, flanges, deletedFlangeIds, searchBox, topCenter, bottomCenter, result);

            // 5) 대상 배관(Exhaust_Pumping)의 열린 커넥터 수집
            //    (앞 단계에서 플랜지를 지웠으므로, 그 자리 배관 커넥터가 열린 상태가 된다)
            List<ConnRef> pipeConns = CollectOpenConnectorsInBox(doc, targetPipes, searchBox);

            // 슬리브의 Primary 커넥터가 위쪽을 향하므로, 상부 배관 커넥터를 먼저 확정한다.
            ConnRef topPipe = FindNearest(pipeConns, topCenter, null);
            ConnRef bottomPipe = FindNearest(pipeConns, bottomCenter, topPipe);

            if (topPipe == null)
            {
                if (LogUtils.DetailEnabled)
                    LogUtils.LogDetail($"슬리브(Id={sleeveId}) 탐색 박스 안에서 대상 배관 커넥터를 찾지 못함.");

                result.NoPipeSleeveCount++;
                return;
            }

            // 6) 슬리브 Primary 커넥터를 상부 배관 커넥터에 연결 (슬리브가 이동/회전)
            Connector primary = ElementUtils.GetPrimaryConnector(sleeve);
            if (primary == null)
            {
                result.FailedCount++;
                return;
            }

            // 이동 후에 다시 찾기 위해, 남은 열린 커넥터의 Id 를 미리 기억
            int remainingConnId = FindRemainingOpenConnectorId(sleeve, primary.Id);

            Connector topConn = topPipe.Resolve(doc);
            if (topConn == null || topConn.IsConnected)
            {
                result.FailedCount++;
                return;
            }

            try
            {
                // 배관 커넥터를 기준(Main)으로 두고, 슬리브(Sub)를 움직여 맞춘다.
                ConnectorHelper.AlignAndConnect(doc, topConn, primary, sleeveId);
                doc.Regenerate();
                result.TopConnectedCount++;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"슬리브(Id={sleeveId}) 상부 배관 연결 실패.");
                result.FailedCount++;
                return;
            }

            // 7) 남은 열린 커넥터를 하부 배관 커넥터에 연결
            //    (슬리브는 이미 상부에 고정됐으므로 더 움직이지 않고 연결만 시도한다)
            if (bottomPipe == null || remainingConnId < 0) return;

            Connector remaining = ElementUtils.ResolveConnector(doc, sleeveId, remainingConnId);
            Connector bottomConn = bottomPipe.Resolve(doc);

            if (remaining == null || bottomConn == null) return;
            if (remaining.IsConnected || bottomConn.IsConnected) return;

            try
            {
                bottomConn.ConnectTo(remaining);
                result.BottomConnectedCount++;
            }
            catch (Exception ex)
            {
                // 두 커넥터 위치가 맞지 않으면 Revit 이 연결을 거부한다.
                LogUtils.LogError(ex, $"슬리브(Id={sleeveId}) 하부 배관 연결 실패.");
                result.FailedCount++;
            }
        }

        /// <summary>
        /// 현재 뷰의 DC FLANGE 후보를 한 번만 수집한다. (중심점까지 미리 구해 둔다)
        /// </summary>
        private static List<FlangeRef> CollectFlanges(Document doc, View view)
        {
            var list = new List<FlangeRef>();

            foreach (FamilyInstance fi in ElementUtils.CollectFamilyInstances(doc, view, FamilyKeywords.DcFlange))
            {
                ElementUtils.WorldBox box = ElementUtils.GetWorldBox(fi);
                if (box == null) continue;

                list.Add(new FlangeRef { Id = fi.Id, Center = box.Center });
            }

            return list;
        }

        /// <summary>
        /// 탐색 박스 안의 DC FLANGE 중, 상부면 중심점 / 하부면 중심점에 가장 가까운 객체를 삭제.
        /// 두 기준이 같은 객체를 가리키면 1개만 삭제하고, 대상이 없으면 아무것도 하지 않는다.
        /// </summary>
        private static void DeleteNearestFlanges(Document doc,
            List<FlangeRef> flanges, HashSet<ElementId> deletedFlangeIds,
            ElementUtils.WorldBox searchBox, XYZ topCenter, XYZ bottomCenter, RunResult result)
        {
            // 중심점이 탐색 박스 안에 들어가는 (아직 지우지 않은) DC FLANGE 고르기
            var candidates = new List<FlangeRef>();

            foreach (FlangeRef flange in flanges)
            {
                if (deletedFlangeIds.Contains(flange.Id)) continue;
                if (!searchBox.Contains(flange.Center)) continue;

                candidates.Add(flange);
            }

            if (candidates.Count == 0) return;

            int nearTopIndex = FindNearestIndex(candidates, topCenter);
            int nearBottomIndex = FindNearestIndex(candidates, bottomCenter);

            var deleteIds = new List<ElementId>();
            if (nearTopIndex >= 0) deleteIds.Add(candidates[nearTopIndex].Id);
            if (nearBottomIndex >= 0 && nearBottomIndex != nearTopIndex) deleteIds.Add(candidates[nearBottomIndex].Id);

            if (deleteIds.Count == 0) return;

            doc.Delete(deleteIds);
            doc.Regenerate(); // 삭제 결과를 반영해야 배관 커넥터가 열린 상태로 조회된다

            foreach (ElementId id in deleteIds)
            {
                deletedFlangeIds.Add(id);
            }

            result.DeletedFlangeCount += deleteIds.Count;
        }

        /// <summary>
        /// System Type 이 "Exhaust_Pumping" 인 배관을 한 번만 수집한다. (끝점까지 미리 구해 둔다)
        /// </summary>
        private static List<PipeRef> CollectTargetPipes(Document doc, View view)
        {
            var list = new List<PipeRef>();

            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe));

            foreach (Element e in collector)
            {
                var pipe = e as Pipe;
                if (pipe == null) continue;

                // System Type 이름 확인
                if (!HasTargetSystemType(doc, pipe)) continue;

                var lc = pipe.Location as LocationCurve;
                if (lc == null || lc.Curve == null) continue;

                list.Add(new PipeRef
                {
                    Id = pipe.Id,
                    End0 = lc.Curve.GetEndPoint(0),
                    End1 = lc.Curve.GetEndPoint(1)
                });
            }

            return list;
        }

        /// <summary>
        /// 끝점이 탐색 박스 안에 하나라도 있는 대상 배관에서, 지금 열려 있는 End 커넥터를 모은다.
        /// (연결하려면 열려 있어야 하므로 이미 연결된 커넥터는 제외한다)
        /// 열림/닫힘은 처리 도중 계속 바뀌므로 슬리브마다 다시 조회한다.
        /// </summary>
        private static List<ConnRef> CollectOpenConnectorsInBox(Document doc, List<PipeRef> targetPipes,
            ElementUtils.WorldBox searchBox)
        {
            var list = new List<ConnRef>();

            foreach (PipeRef pipeRef in targetPipes)
            {
                // 끝점 중 하나라도 탐색 박스 안에 있어야 대상
                if (!searchBox.Contains(pipeRef.End0) && !searchBox.Contains(pipeRef.End1)) continue;

                Element pipe = doc.GetElement(pipeRef.Id);
                if (pipe == null) continue;

                foreach (Connector c in ElementUtils.GetOpenEndConnectors(pipe))
                {
                    list.Add(ConnRef.From(pipeRef.Id, c));
                }
            }

            return list;
        }

        /// <summary>
        /// 배관의 System Type 이름이 대상("Exhaust_Pumping")인지 검사(대소문자 무시).
        /// </summary>
        private static bool HasTargetSystemType(Document doc, Pipe pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            if (p == null) return false;

            Element systemType = doc.GetElement(p.AsElementId());
            if (systemType == null) return false;

            return string.Equals(systemType.Name, TargetSystemTypeName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 슬리브에서 Primary 가 아닌 열린 End 커넥터의 Id 를 반환. 없으면 -1.
        /// </summary>
        private static int FindRemainingOpenConnectorId(FamilyInstance sleeve, int primaryConnectorId)
        {
            foreach (Connector c in ElementUtils.GetOpenEndConnectors(sleeve))
            {
                if (c.Id == primaryConnectorId) continue;
                return c.Id;
            }

            return -1;
        }

        /// <summary>
        /// 기준점에 가장 가까운 커넥터 참조를 반환. exclude 로 지정한 것은 제외. 없으면 null.
        /// </summary>
        private static ConnRef FindNearest(List<ConnRef> items, XYZ target, ConnRef exclude)
        {
            ConnRef best = null;
            double bestDist = double.MaxValue;

            foreach (ConnRef item in items)
            {
                if (exclude != null && ReferenceEquals(item, exclude)) continue;

                double dist = item.Origin.DistanceTo(target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = item;
                }
            }

            return best;
        }

        /// <summary>
        /// 기준점에 가장 가까운 플랜지의 인덱스를 반환. 목록이 비어 있으면 -1.
        /// </summary>
        private static int FindNearestIndex(List<FlangeRef> flanges, XYZ target)
        {
            int bestIndex = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < flanges.Count; i++)
            {
                double dist = flanges[i].Center.DistanceTo(target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
    }
}
