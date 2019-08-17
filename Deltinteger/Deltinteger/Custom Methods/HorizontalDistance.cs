﻿using System;
using Deltin.Deltinteger.WorkshopWiki;

namespace Deltin.Deltinteger.Elements
{
    [CustomMethod("HorizontalDistance", CustomMethodType.Value)]
    [Parameter("point1", ValueType.Vector, null)]
    [Parameter("point2", ValueType.Vector, null)]
    class HorizontalDistance : CustomMethodBase
    {
        protected override MethodResult Get()
        {
            Element point1 = (Element)Parameters[0];
            Element point2 = (Element)Parameters[1];
            Element x = Element.Part<V_Subtract>(Element.Part<V_XOf>(point1), Element.Part<V_XOf>(point2));
            Element z = Element.Part<V_Subtract>(Element.Part<V_ZOf>(point1), Element.Part<V_ZOf>(point2));
            Element sum = Element.Part<V_Add>(Element.Part<V_RaiseToPower>(x, new V_Number(2)), Element.Part<V_RaiseToPower>(z, new V_Number(2)));
            return new MethodResult(null, Element.Part<V_SquareRoot>(sum));
        }

        public override WikiMethod Wiki()
        {
            return new WikiMethod("HorizontalDistance", "The distance between 2 points as if they were on the same Y level.", null);
        }
    }
}