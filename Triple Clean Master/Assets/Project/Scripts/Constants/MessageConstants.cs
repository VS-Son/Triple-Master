namespace Project.Scripts.Constants
{
    public class MessageConstants
    {
        public static string MessageNotUndo = "Ôi, không có bước đi nào để hoàn tác";
        public static string MessageAdsNotReady = "Quảng cáo chưa sẵn sàng, vui lòng thử lại sau";
        public static string MessageUnlock(int level)
       {
           return $"Tính năng mới sẽ được mở khóa tại cấp độ {level}."; 
       }  
       
    }
}
