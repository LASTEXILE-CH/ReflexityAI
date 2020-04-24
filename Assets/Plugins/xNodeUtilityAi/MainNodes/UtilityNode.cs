﻿using System;
using Plugins.xNodeUtilityAi.MiddleNodes;
using UnityEngine;
using XNode;

namespace Plugins.xNodeUtilityAi.MainNodes {
    public class UtilityNode : MiddleNode {
        
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override), Tooltip("Scale the 0 on the X axe")] 
        public int MinX;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override), Tooltip("Evaluated between Min X and Max X")] 
        public int X;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override), Tooltip("Scale the 1 on the X axe")] 
        public int MaxX = 1;
        [Tooltip("Evaluate the Utility Y using the X values")]
        public AnimationCurve Function = AnimationCurve.Linear(0, 0, 1, 1);
//        public int MinY = -5;
//        public int MaxY = 5;
        [Tooltip("Connect to the Option Node")]
        [Output(connectionType: ConnectionType.Override)] public int Rank;
        
        public override object GetValue(NodePort port) {
            if (port.fieldName == nameof(Rank)) {
                int minX = GetInputValue(nameof(MinX), MinX);
                int maxX = GetInputValue(nameof(MaxX), MaxX);
                int x = GetInputValue(nameof(X), X);
                float scaledX = ScaleX(minX, maxX, x);
                return scaledX;
//                return ScaleY(MinY, MaxY, Function.Evaluate(scaledX));
            }
            return null;
        }
        
        private float ScaleX(float minX, float maxX, float x) {
            if (Math.Abs(minX - maxX) <= 0) return 0;
            if (x < minX) x = minX;
            if (x > maxX) x = maxX;
            return (x - minX) / (maxX - minX);
        }
        
        private int ScaleY(int minY, int maxY, float y) {
            if (Math.Abs(minY - maxY) <= 0) return 0;
            return (int) ((maxY - minY) * y - minY);
        }
        
    }
}
