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

            // 첫 객체(시작 커넥터) 기준으로 System Type / FLEX PIPE 타입 / 레벨 결정
            ElementId systemTypeId = GetSystemTypeId(obj1);
            ElementId flexPipeTypeId = GetFlexPipeTypeId(doc);
            ElementId levelId = ElementUtils.FindNearestLevelId(doc, startConn.Origin);

            FlexPipe flexPipe = FlexPipe.Create(
                doc, systemTypeId, flexPipeTypeId, levelId,
                startConn.Origin, endConn.Origin, new List<XYZ>());

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
        /// 새로 만든 FlexPipe 의 커넥터 중 targetConn 원점과 일치하는 것을 찾아 연결한다.
        /// </summary>
        private static void ConnectFlexPipeEnd(FlexPipe flexPipe, Connector targetConn)
        {
            foreach (Connector c in ElementUtils.GetEndConnectors(flexPipe))
            {
                if (c.Origin.DistanceTo(targetConn.Origin) < 1e-6)
                {
                    if (!c.IsConnectedTo(targetConn))
                        c.ConnectTo(targetConn);
                    return;
                }
            }
        }

        /// <summary>
        /// 객체의 System Type(Id)을 구한다. 없으면 예외.
        /// </summary>
        private static ElementId GetSystemTypeId(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            if (p == null || p.AsElementId() == ElementId.InvalidElementId)
                throw new InvalidOperationException("첫 번째 객체에서 System Type 을 확인할 수 없습니다.");

            return p.AsElementId();
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
