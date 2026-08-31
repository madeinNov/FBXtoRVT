using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// "Flex Pipe 생성기" 기능의 핵심 로직.
    ///
    /// 사용자가 첫 객체 → 둘째 객체를 차례로 고르면, 첫 객체의 열린 커넥터에서
    /// 둘째 객체의 열린 커넥터까지 FLEX PIPE("METAL HOSE_STS304(FLEX)" 타입)를 생성한다.
    /// 지름 / System Type 은 첫 객체(정확히는 사용된 커넥터) 기준으로 맞춘다.
    /// 첫 객체에 System Type 이 없으면(= Undefined) 막지 않고 Undefined 인 채로 만든다.
    ///
    /// 커넥터 선택 규칙
    ///  - 둘 중 하나라도 열린 커넥터가 없으면 실행하지 않는다.
    ///  - 각 객체에 열린 커넥터가 여러 개면, 두 객체의 커넥터 조합 중 거리가 가장 가까운 쌍을 사용한다.
    ///    (= "서로의 객체와 가까운 커넥터" 로 연결)
    /// </summary>
    public static class FlexPipeHelper
    {
        // 사용할 FLEX PIPE 타입 이름
        private const string FlexPipeTypeName = "METAL HOSE_STS304(FLEX)";

        // System Type 을 못 찾았을 때 새로 만들 "미지정" 배관 시스템 타입 이름
        private const string UndefinedSystemTypeName = "Undefined";

        // 이보다 짧으면 배관을 만들지 않는다. (1mm)
        private static readonly double MinLengthFeet = ElementUtils.MmToFeet(1.0);

        /// <summary>
        /// Flex Pipe 를 생성한다. (외부에서 Transaction 을 열고 호출)
        /// 조건이 맞지 않으면 InvalidOperationException 을 던져 호출한 쪽에서 안내하도록 한다.
        /// </summary>
        public static FlexPipe CreateFlexPipe(Document doc, Element obj1, Element obj2)
        {
            List<Connector> conns1 = ElementUtils.GetOpenEndConnectors(obj1);
            List<Connector> conns2 = ElementUtils.GetOpenEndConnectors(obj2);

            if (conns1.Count == 0)
                throw new InvalidOperationException("첫 번째 객체에 열린 커넥터가 없습니다.");
            if (conns2.Count == 0)
                throw new InvalidOperationException("두 번째 객체에 열린 커넥터가 없습니다.");

            // 두 객체의 열린 커넥터 조합 중 가장 가까운 쌍을 사용
            Connector startConn = null;
            Connector endConn = null;
            double bestDist = double.MaxValue;

            foreach (Connector c1 in conns1)
            {
                foreach (Connector c2 in conns2)
                {
                    double dist = c1.Origin.DistanceTo(c2.Origin);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        startConn = c1;
                        endConn = c2;
                    }
                }
            }

            // 두 커넥터가 사실상 같은 자리에 있으면 배관을 만들 수 없다.
            if (bestDist < MinLengthFeet)
                throw new InvalidOperationException(
                    "두 객체의 커넥터가 너무 가까워서 FLEX PIPE 를 만들 수 없습니다.");

            // 첫 객체(시작 커넥터) 기준으로 System Type / FLEX PIPE 타입 / 레벨 결정
            ElementId systemTypeId = ResolveSystemTypeId(doc, obj1);
            ElementId flexPipeTypeId = GetFlexPipeTypeId(doc);
            ElementId levelId = ElementUtils.FindNearestLevelId(doc, startConn.Origin);

            // FlexPipe.Create 의 5·6번째 인자는 "점" 이 아니라 "접선 방향 벡터" 이고,
            // 마지막 points 에 시작점/끝점을 포함한 2개 이상의 점을 넣어야 한다.
            // (예전 코드는 커넥터 원점을 접선 자리에 넣고 points 를 빈 목록으로 넘겨서
            //  "The valid number of points is less than two..." 경고가 났다)
            XYZ startTangent = GetOutwardDirection(startConn);
            XYZ endTangent = GetOutwardDirection(endConn).Negate(); // 끝에서는 커넥터로 "들어가는" 방향

            var points = new List<XYZ> { startConn.Origin, endConn.Origin };

            FlexPipe flexPipe = FlexPipe.Create(
                doc, systemTypeId, flexPipeTypeId, levelId,
                startTangent, endTangent, points);

            // Pipe.Create 와 마찬가지로, 끝점이 기존 열린 커넥터 위치와 정확히 일치하면
            // 자동으로 연결되지 않는 경우가 있으므로 명시적으로 연결한다.
            ConnectFlexPipeEnd(flexPipe, startConn);
            ConnectFlexPipeEnd(flexPipe, endConn);

            // 지름을 첫 객체(시작 커넥터) 기준으로 맞춘다.
            Parameter diaParam = flexPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && !diaParam.IsReadOnly)
            {
                diaParam.Set(startConn.Radius * 2.0);
            }

            return flexPipe;
        }

        /// <summary>
        /// 커넥터가 바깥(객체에서 나가는 쪽)을 향하는 방향 벡터.
        /// Revit 커넥터의 좌표계 Z축이 곧 바깥 방향이다.
        /// </summary>
        private static XYZ GetOutwardDirection(Connector c)
        {
            return c.CoordinateSystem.BasisZ.Normalize();
        }

        /// <summary>
        /// 새로 만든 FlexPipe 의 커넥터 중 targetConn 원점과 일치하는 것을 찾아 연결한다.
        /// </summary>
        private static void ConnectFlexPipeEnd(FlexPipe flexPipe, Connector targetConn)
        {
            // 생성 직후 끝점이 아주 조금 어긋날 수 있으므로 1mm 정도는 같은 자리로 본다.
            double tolerance = ElementUtils.MmToFeet(1.0);

            foreach (Connector c in ElementUtils.GetEndConnectors(flexPipe))
            {
                if (c.Origin.DistanceTo(targetConn.Origin) < tolerance)
                {
                    if (!c.IsConnectedTo(targetConn))
                        c.ConnectTo(targetConn);
                    return;
                }
            }
        }

        /// <summary>
        /// 새로 만들 FLEX PIPE 에 쓸 System Type 을 정한다.
        ///
        /// 첫 객체가 System Type 을 갖고 있으면 그대로 따라간다.
        /// 갖고 있지 않으면(= Undefined) 예전에는 예외를 던져서 배관을 아예 만들지 못했는데,
        /// 지금은 <b>Undefined(미지정) System Type 으로 그냥 만든다.</b>
        ///
        /// 찾는 순서
        ///  1) 첫 객체의 System Type
        ///  2) 문서에 있는 "분류가 Undefined" 인 배관 시스템 타입
        ///  3) 이름이 Undefined / 미지정 인 배관 시스템 타입
        ///  4) 그런 게 없으면 새로 하나 만든다
        ///  5) 그것마저 안 되면 문서의 아무 배관 시스템 타입 (배관 생성 자체는 되도록)
        /// </summary>
        private static ElementId ResolveSystemTypeId(Document doc, Element e)
        {
            // 1) 첫 객체가 들고 있는 System Type
            Parameter p = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            if (p != null)
            {
                ElementId id = p.AsElementId();
                if (id != null && id != ElementId.InvalidElementId)
                    return id;
            }

            LogUtils.Log("FLEX PIPE: 첫 객체에 System Type 이 없어 Undefined 로 만듭니다.");

            var pipingSystemTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .ToList();

            // 2) 분류가 Undefined 인 것
            PipingSystemType undefined = pipingSystemTypes.FirstOrDefault(
                t => t.SystemClassification == MEPSystemClassification.UndefinedSystemClassification);

            // 3) 이름으로 한 번 더 찾아본다 (템플릿에 따라 분류가 다르게 들어간 경우 대비)
            if (undefined == null)
            {
                undefined = pipingSystemTypes.FirstOrDefault(t =>
                    (t.Name ?? "").IndexOf("Undefined", StringComparison.OrdinalIgnoreCase) >= 0
                    || (t.Name ?? "").Contains("미지정"));
            }

            if (undefined != null)
            {
                LogUtils.Log($"FLEX PIPE: Undefined System Type 사용. Id={undefined.Id} 이름='{undefined.Name}'");
                return undefined.Id;
            }

            // 4) 없으면 새로 만든다
            try
            {
                PipingSystemType created = PipingSystemType.Create(
                    doc, MEPSystemClassification.UndefinedSystemClassification, UndefinedSystemTypeName);

                LogUtils.Log($"FLEX PIPE: Undefined System Type 을 새로 만들었습니다. Id={created.Id}");
                return created.Id;
            }
            catch (Exception ex)
            {
                LogUtils.LogError(ex, "Undefined System Type 생성 실패. 문서의 다른 배관 시스템 타입을 씁니다.");
            }

            // 5) 마지막 수단: 아무 배관 시스템 타입이라도 써서 배관은 만들어 준다
            PipingSystemType any = pipingSystemTypes.FirstOrDefault();
            if (any != null)
            {
                LogUtils.Log($"FLEX PIPE: Undefined 를 찾지 못해 '{any.Name}' System Type 으로 만듭니다.");
                return any.Id;
            }

            throw new InvalidOperationException(
                "이 문서에 배관 시스템 타입(Piping System Type)이 하나도 없어 FLEX PIPE 를 만들 수 없습니다.");
        }

        /// <summary>
        /// 이름이 "METAL HOSE_STS304(FLEX)" 인 FlexPipeType 을 찾는다. 없으면 예외.
        /// </summary>
        private static ElementId GetFlexPipeTypeId(Document doc)
        {
            FlexPipeType type = new FilteredElementCollector(doc)
                .OfClass(typeof(FlexPipeType))
                .Cast<FlexPipeType>()
                .FirstOrDefault(t => t.Name == FlexPipeTypeName);

            if (type == null)
                throw new InvalidOperationException($"'{FlexPipeTypeName}' 배관 타입을 찾지 못했습니다.");

            return type.Id;
        }
    }
}
