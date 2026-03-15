using System;

namespace AnimationSystem.Settings
{
    [Serializable]
    public struct IKSettings
    {
        public int RootBoneIndex;
        public int HipBoneIndex;
        public int LeftThighBoneIndex;
        public int LeftShinBoneIndex;
        public int LeftFootBoneIndex;
        public int RightThighBoneIndex;
        public int RightShinBoneIndex;
        public int RightFootBoneIndex;
        public int IKFootLIndex;
        public int IKFootRIndex;
    }
}