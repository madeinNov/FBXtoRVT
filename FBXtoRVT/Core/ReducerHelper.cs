using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "Reducer 생성기" 기능의 핵심 로직.
    ///
    /// [무엇을 하는 기능인가]
    /// 배관 하나를 클릭하면, 그 배관의 <b>열린 커넥터</b> 바깥쪽에
    /// 오토라우팅 리듀서(배관 타입의 Routing Preferences 에 등록된 Transition 패밀리)를 넣는다.
    ///   - 배관쪽 지름  : 클릭한 배관의 Nominal Diameter 를 그대로 따라간다.
    ///   - 반대쪽 지름  : 사용자가 입력한 값(mm).
    ///   - 반대쪽에는 입력한 지름의 배관을 100mm 길이로 함께 만들어 남긴다.
    ///
    /// [왜 배관을 하나 더 만드는가]
    /// Revit API 에는 "커넥터 하나에 리듀서만 꽂기" 라는 기능이 없다.
    /// 리듀서를 만들어 주는 <c>doc.Create.NewTransitionFitting(커넥터A, 커넥터B)</c> 은
    /// <b>지름이 서로 다른 커넥터 2개</b>를 요구하므로, 반대쪽에 목표 지름의 배관이 반드시 필요하다.
    ///
    /// [클릭한 배관은 길이가 변하지 않는다]
    /// 리듀서를 그냥 넣으면 Revit 이 배관 끝을 리듀서 길이만큼 잘라내거나 늘려 버린다.
    /// 그래서 아래 2단계로 처리해 <b>클릭한 배관의 끝점을 그대로 유지</b>한다.
    ///   1) 임시로(SubTransaction) 리듀서를 한 번 만들어 <b>리듀서의 실제 길이</b>를 재고 되돌린다.
    ///      (리듀서 길이는 지름 조합에 따라 달라서 미리 알 수 없다)
    ///   2) 잰 길이만큼 정확히 띄운 자리에 새 배관을 만들고 리듀서를 넣는다.
    ///      빈틈이 리듀서 길이와 딱 맞으므로 Revit 이 배관 길이를 건드릴 이유가 없다.
    ///   3) 그래도 끝점이 움직였으면 원래 위치로 되돌린다. (안전장치)
    /// </summary>
    public static class ReducerHelper
    {
        // 리듀서 반대쪽에 함께 만들어 남길 배관의 길이 (mm)
        private const double NewPipeLengthMm = 100.0;

        // 1단계(리듀서 길이 재기)에서 두 커넥터를 벌려 둘 간격 (mm)
        // 어지간한 리듀서보다 길게 잡아 두면 Revit 이 빈틈을 배관으로 채우며 리듀서를 만들어 준다.
        private const double ProbeGapMm = 300.0;

        // 지름 / 위치가 "같다" 고 볼 오차 (0.1mm)
        private static readonly double ToleranceFeet = ElementUtils.MmToFeet(0.1);

        /// <summary>
        /// 실행 결과 요약. Commands 쪽에서 대화상자로 보여준다.
        /// </summary>
        public class ReducerResult
        {
            /// <summary>새로 만든 리듀서(Transition 패밀리) Id</summary>
            public ElementId ReducerId;

            /// <summary>리듀서 반대쪽에 함께 만든 배관 Id</summary>
            public ElementId NewPipeId;

            /// <summary>클릭한 배관의 지름 (mm)</summary>
            public double SourceNdMm;

            /// <summary>사용자가 입력한 반대쪽 지름 (mm)</summary>
            public double TargetNdMm;

            /// <summary>실제로 들어간 리듀서의 길이 (mm)</summary>
            public double ReducerLengthMm;

            /// <summary>클릭한 배관의 끝점이 움직여서 되돌려야 했는지</summary>
            public bool PipeEndRestored;
        }

        /// <summary>
        /// 배관의 열린 커넥터에 리듀서를 넣는다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        /// <param name="pipe">사용자가 클릭한 배관</param>
        /// <param name="clickPoint">사용자가 클릭한 지점. 열린 커넥터가 여러 개일 때 어느 쪽인지 고르는 데 쓴다.</param>
        /// <param name="targetNdMm">리듀서 반대쪽 지름(Nominal Diameter, mm)</param>
        public static ReducerResult CreateReducer(Document doc, Pipe pipe, XYZ clickPoint, double targetNdMm)
        {
            var result = new ReducerResult();

            // ===== 1) 리듀서를 넣을 열린 커넥터 고르기 =====
            Connector targetConn = PickOpenConnector(pipe, clickPoint);

            ElementId pipeId = pipe.Id;
            int targetConnId = targetConn.Id;
            XYZ connOrigin = targetConn.Origin;
            XYZ outward = targetConn.CoordinateSystem.BasisZ.Normalize(); // 배관 바깥으로 나가는 방향

            // 나중에 되돌리기 위해 클릭한 배관의 원래 중심선을 기억해 둔다.
            Line originalLine = GetPipeLine(pipe);

            // ===== 2) 입력값 검사 =====
            double sourceDiameter = pipe.Diameter;                       // 클릭한 배관의 ND (feet)
            double targetDiameter = ElementUtils.MmToFeet(targetNdMm);   // 입력한 ND (feet)

            result.SourceNdMm = FeetToMm(sourceDiameter);
            result.TargetNdMm = targetNdMm;

            if (Math.Abs(targetDiameter - sourceDiameter) < ToleranceFeet)
            {
                throw new InvalidOperationException(
                    $"입력한 지름({targetNdMm:F0}mm)이 클릭한 배관의 지름({result.SourceNdMm:F0}mm)과 같습니다.\n" +
                    "지름이 달라야 리듀서를 넣을 수 있습니다.");
            }

            var pipeType = doc.GetElement(pipe.GetTypeId()) as PipeType;
            if (pipeType == null)
                throw new InvalidOperationException("클릭한 객체의 배관 타입을 찾지 못했습니다.");

            CheckTransitionRuleExists(pipeType);
            CheckSizeIsAvailable(doc, pipeType, targetDiameter, targetNdMm);

            LogUtils.Log($"===== Reducer 생성기 시작. 배관 Id={pipeId} " +
                $"{result.SourceNdMm:F0}mm -> {targetNdMm:F0}mm 커넥터={FormatXyz(connOrigin)} =====");

            // ===== 3) 1단계: 리듀서의 실제 길이를 재고 되돌린다 =====
            double reducerLengthFeet = 0.0;   // 잰 리듀서 길이
            double workingGapFeet = 0.0;      // 1단계에서 실제로 통했던 간격
            InvalidOperationException probeError = null;

            // 간격을 얼마로 벌려야 Revit 이 리듀서를 만들어 주는지는 버전 / 패밀리에 따라 다르므로,
            // 넓은 간격부터 차례로 줄여 가며 시도한다. (성공한 한 번의 결과만 쓰고 모두 되돌린다)
            foreach (double gapMm in new[] { ProbeGapMm, 100.0, 0.0 })
            {
                using (SubTransaction probe = new SubTransaction(doc))
                {
                    probe.Start();

                    try
                    {
                        BuildOutcome probeOutcome = BuildNewPipeAndReducer(
                            doc, pipeId, targetConnId, connOrigin, outward, targetDiameter,
                            ElementUtils.MmToFeet(gapMm));

                        reducerLengthFeet = probeOutcome.ReducerLengthFeet;
                        workingGapFeet = ElementUtils.MmToFeet(gapMm);
                        probeError = null;
                    }
                    catch (InvalidOperationException ex)
                    {
                        probeError = ex;
                        LogUtils.Log($"  리듀서 길이 재기 실패(간격 {gapMm:F0}mm). 간격을 바꿔 다시 시도합니다.");
                    }
                    finally
                    {
                        probe.RollBack();   // 잰 길이만 남기고 문서는 원래대로
                    }
                }

                if (probeError == null) break;
            }

            if (probeError != null) throw probeError;

            LogUtils.Log($"  리듀서 길이 측정값 = {FeetToMm(reducerLengthFeet):F1}mm");

            // ===== 4) 2단계: 리듀서 길이만큼 정확히 띄운 자리에 실제로 만든다 =====
            // 이렇게 하면 빈틈이 리듀서와 딱 맞아서 Revit 이 배관 길이를 건드릴 이유가 없다.
            double finalGapFeet = reducerLengthFeet;

            // 길이를 재지 못했으면(0에 가까우면) 1단계에서 통했던 간격을 그대로 쓴다.
            if (finalGapFeet < doc.Application.ShortCurveTolerance)
                finalGapFeet = workingGapFeet;

            BuildOutcome outcome = null;

            using (SubTransaction build = new SubTransaction(doc))
            {
                build.Start();

                try
                {
                    outcome = BuildNewPipeAndReducer(
                        doc, pipeId, targetConnId, connOrigin, outward, targetDiameter, finalGapFeet);

                    build.Commit();
                }
                catch (InvalidOperationException ex)
                {
                    build.RollBack();
                    outcome = null;
                    LogUtils.Log($"  딱 맞는 간격({FeetToMm(finalGapFeet):F1}mm)으로는 실패했습니다. " +
                        $"1단계에서 통했던 간격으로 다시 만듭니다. ({ex.Message})");
                }
            }

            // 딱 맞는 간격으로 실패했으면, 1단계에서 통했던 간격으로 만든다.
            // (이때는 배관 끝점이 움직일 수 있는데, 바로 아래 5) 에서 되돌린다)
            if (outcome == null)
            {
                outcome = BuildNewPipeAndReducer(
                    doc, pipeId, targetConnId, connOrigin, outward, targetDiameter, workingGapFeet);
            }

            result.ReducerId = outcome.ReducerId;
            result.NewPipeId = outcome.NewPipeId;
            result.ReducerLengthMm = FeetToMm(outcome.ReducerLengthFeet);

            // ===== 5) 안전장치: 클릭한 배관의 끝점이 움직였으면 되돌린다 =====
            result.PipeEndRestored = RestorePipeLineIfMoved(doc, pipeId, originalLine);

            LogUtils.Log($"===== Reducer 생성기 종료. 리듀서 Id={result.ReducerId} " +
                $"새배관 Id={result.NewPipeId} 리듀서길이={result.ReducerLengthMm:F1}mm " +
                $"배관끝점되돌림={result.PipeEndRestored} =====");

            return result;
        }

        // ===== 1) 커넥터 고르기 =====

        /// <summary>
        /// 배관의 열린 커넥터 중 클릭한 지점에 가장 가까운 것을 고른다.
        /// 열린 커넥터가 하나도 없으면 예외를 던진다.
        /// </summary>
        private static Connector PickOpenConnector(Pipe pipe, XYZ clickPoint)
        {
            List<Connector> openConns = ElementUtils.GetOpenEndConnectors(pipe);

            if (openConns.Count == 0)
                throw new InvalidOperationException(
                    "클릭한 배관에 열린 커넥터가 없습니다.\n" +
                    "양쪽 끝이 모두 다른 객체에 연결돼 있으면 리듀서를 넣을 자리가 없습니다.");

            if (openConns.Count == 1 || clickPoint == null)
                return openConns[0];

            // 클릭한 지점에 가까운 쪽 = 사용자가 리듀서를 넣고 싶어 하는 쪽
            Connector best = openConns[0];
            double bestDist = double.MaxValue;

            foreach (Connector c in openConns)
            {
                double dist = c.Origin.DistanceTo(clickPoint);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        // ===== 2) 입력값 검사 =====

        /// <summary>
        /// 배관 타입의 Routing Preferences 에 리듀서(Transition) 규칙이 있는지 검사.
        /// 없으면 NewTransitionFitting 이 실패하므로 미리 안내한다.
        /// </summary>
        private static void CheckTransitionRuleExists(PipeType pipeType)
        {
            RoutingPreferenceManager rpm = pipeType.RoutingPreferenceManager;

            if (rpm == null || rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Transitions) == 0)
            {
                throw new InvalidOperationException(
                    $"배관 타입 '{pipeType.Name}' 에 리듀서(Transition) 패밀리가 지정돼 있지 않습니다.\n" +
                    "배관 타입 편집 > Routing Preferences 의 Transition 항목에 리듀서 패밀리를 넣어 주세요.");
            }
        }

        /// <summary>
        /// 입력한 지름이 그 배관 타입에서 실제로 쓸 수 있는 사이즈인지 검사.
        /// 목록에 없으면 쓸 수 있는 사이즈를 함께 알려 주고 중단한다.
        /// </summary>
        private static void CheckSizeIsAvailable(Document doc, PipeType pipeType,
            double targetDiameter, double targetNdMm)
        {
            List<double> availableDiameters = GetAvailableNominalDiameters(doc, pipeType);

            // 사이즈 목록을 못 읽었으면(비정상 타입 등) 막지 않고 그대로 진행한다.
            if (availableDiameters.Count == 0)
            {
                LogUtils.Log($"  배관 타입 '{pipeType.Name}' 의 사이즈 목록을 읽지 못해 검사를 건너뜁니다.");
                return;
            }

            bool exists = availableDiameters.Any(d => Math.Abs(d - targetDiameter) < ToleranceFeet);
            if (exists) return;

            string sizeList = string.Join(", ",
                availableDiameters.Select(d => FeetToMm(d)).OrderBy(mm => mm).Select(mm => $"{mm:F0}"));

            throw new InvalidOperationException(
                $"배관 타입 '{pipeType.Name}' 에는 {targetNdMm:F0}mm 사이즈가 없습니다.\n\n" +
                $"쓸 수 있는 지름(mm): {sizeList}");
        }

        /// <summary>
        /// 배관 타입의 Routing Preferences 에 등록된 배관 세그먼트들이 지원하는
        /// Nominal Diameter 목록(feet)을 모아서 반환한다.
        /// </summary>
        private static List<double> GetAvailableNominalDiameters(Document doc, PipeType pipeType)
        {
            var diameters = new List<double>();

            RoutingPreferenceManager rpm = pipeType.RoutingPreferenceManager;
            if (rpm == null) return diameters;

            int ruleCount = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);

            for (int i = 0; i < ruleCount; i++)
            {
                RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                if (rule == null) continue;

                var segment = doc.GetElement(rule.MEPPartId) as PipeSegment;
                if (segment == null) continue;

                foreach (MEPSize size in segment.GetSizes())
                {
                    double nd = size.NominalDiameter;

                    // 세그먼트가 여러 개면 같은 사이즈가 중복될 수 있으므로 한 번만 담는다.
                    if (!diameters.Any(d => Math.Abs(d - nd) < ToleranceFeet))
                        diameters.Add(nd);
                }
            }

            return diameters;
        }

        // ===== 3·4) 새 배관 + 리듀서 만들기 =====

        /// <summary>
        /// 한 번의 "새 배관 + 리듀서" 생성 결과.
        /// </summary>
        private class BuildOutcome
        {
            public ElementId ReducerId;
            public ElementId NewPipeId;
            public double ReducerLengthFeet;
        }

        /// <summary>
        /// 커넥터 바깥쪽으로 <paramref name="gapFeet"/> 만큼 띄운 자리에 목표 지름의 배관을 만들고,
        /// 클릭한 배관과 그 배관 사이에 오토라우팅 리듀서를 넣는다.
        ///
        /// 요소를 만들 때마다 문서가 바뀌므로, 커넥터는 Id 로 매번 다시 찾아 쓴다.
        /// </summary>
        /// <param name="gapFeet">클릭한 배관의 커넥터와 새 배관 사이에 비워 둘 간격 = 리듀서가 들어갈 자리</param>
        private static BuildOutcome BuildNewPipeAndReducer(Document doc, ElementId pipeId, int connId,
            XYZ connOrigin, XYZ outward, double targetDiameter, double gapFeet)
        {
            var outcome = new BuildOutcome();

            var basePipe = doc.GetElement(pipeId) as Pipe;
            if (basePipe == null)
                throw new InvalidOperationException("클릭한 배관을 찾지 못했습니다.");

            // 새 배관은 클릭한 배관의 타입 / System Type / 레벨을 그대로 따라간다.
            ElementId pipeTypeId = basePipe.GetTypeId();

            Parameter systemParam = basePipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            ElementId systemTypeId = (systemParam != null)
                ? systemParam.AsElementId()
                : ElementId.InvalidElementId;

            Parameter levelParam = basePipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            ElementId levelId = (levelParam != null)
                ? levelParam.AsElementId()
                : ElementId.InvalidElementId;

            if (levelId == ElementId.InvalidElementId)
                levelId = ElementUtils.FindNearestLevelId(doc, connOrigin);

            // 새 배관 위치: 커넥터에서 리듀서 자리만큼 띄운 곳부터 100mm
            XYZ newPipeStart = connOrigin + outward * gapFeet;
            XYZ newPipeEnd = newPipeStart + outward * ElementUtils.MmToFeet(NewPipeLengthMm);

            Pipe newPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, newPipeStart, newPipeEnd);
            outcome.NewPipeId = newPipe.Id;

            // 지름을 입력값으로 맞춘다. (리듀서는 이 지름 차이를 보고 만들어진다)
            Parameter diaParam = newPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
                diaParam.Set(targetDiameter);

            doc.Regenerate();

            // 지름을 바꾼 뒤라 커넥터를 다시 찾아온다.
            Connector pipeConn = ElementUtils.ResolveConnector(doc, pipeId, connId);
            Connector newPipeConn = ElementUtils.FindNearestEndConnector(newPipe, connOrigin);

            if (pipeConn == null || newPipeConn == null)
                throw new InvalidOperationException("리듀서를 넣을 커넥터를 찾지 못했습니다.");

            FamilyInstance reducer;
            try
            {
                reducer = doc.Create.NewTransitionFitting(pipeConn, newPipeConn);
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"리듀서 삽입 실패. 배관 Id={pipeId} 새배관 Id={outcome.NewPipeId}");
                throw new InvalidOperationException(
                    "리듀서를 넣지 못했습니다.\n" +
                    "배관 타입의 Routing Preferences 에 이 지름 조합을 처리할 리듀서 패밀리가 있는지 확인해 주세요.\n\n" +
                    $"(Revit 메시지: {ex.Message})");
            }

            doc.Regenerate();

            outcome.ReducerId = reducer.Id;
            outcome.ReducerLengthFeet = MeasureFittingLength(reducer);

            return outcome;
        }

        /// <summary>
        /// 리듀서의 길이 = 양쪽 끝 커넥터 사이의 거리.
        /// 커넥터가 2개 미만이면 0 을 반환한다.
        /// </summary>
        private static double MeasureFittingLength(FamilyInstance fitting)
        {
            List<Connector> conns = ElementUtils.GetEndConnectors(fitting);
            if (conns.Count < 2) return 0.0;

            return conns[0].Origin.DistanceTo(conns[1].Origin);
        }

        // ===== 5) 배관 끝점 되돌리기 =====

        /// <summary>
        /// 리듀서를 넣는 과정에서 클릭한 배관의 중심선이 바뀌었으면 원래대로 되돌린다.
        /// (배관 끝을 되돌리면 거기에 붙어 있는 리듀서와 새 배관이 함께 따라 움직인다)
        /// </summary>
        /// <returns>실제로 되돌렸으면 true</returns>
        private static bool RestorePipeLineIfMoved(Document doc, ElementId pipeId, Line originalLine)
        {
            if (originalLine == null) return false;

            var pipe = doc.GetElement(pipeId) as Pipe;
            if (pipe == null) return false;

            var lc = pipe.Location as LocationCurve;
            Line currentLine = (lc != null) ? lc.Curve as Line : null;
            if (currentLine == null) return false;

            bool sameStart = currentLine.GetEndPoint(0).DistanceTo(originalLine.GetEndPoint(0)) < ToleranceFeet;
            bool sameEnd = currentLine.GetEndPoint(1).DistanceTo(originalLine.GetEndPoint(1)) < ToleranceFeet;

            if (sameStart && sameEnd) return false;   // 안 움직였으면 그대로 둔다

            try
            {
                lc.Curve = originalLine;
                doc.Regenerate();

                LogUtils.Log($"  클릭한 배관(Id={pipeId})의 끝점이 움직여서 원래 위치로 되돌렸습니다.");
                return true;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, $"배관(Id={pipeId}) 끝점 되돌리기 실패.");
                return false;
            }
        }

        // ===== 공통 =====

        /// <summary>배관의 중심선(직선)을 반환. 직선이 아니면 예외.</summary>
        private static Line GetPipeLine(Pipe pipe)
        {
            var lc = pipe.Location as LocationCurve;
            Line line = (lc != null) ? lc.Curve as Line : null;

            if (line == null)
                throw new InvalidOperationException("클릭한 배관이 직선이 아니라 리듀서를 넣을 수 없습니다.");

            return line;
        }

        /// <summary>Revit 내부 단위(feet)를 mm 로 변환.</summary>
        private static double FeetToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        private static string FormatXyz(XYZ p)
        {
            return p == null ? "null" : $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }
    }
}
