namespace devMobile.TheThingsNetwork.ModelsRaw
{
   using System.Collections.Generic;

   // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
   public class ApplicationIds
   {
      public string application_id { get; set; }
   }

   public class EndDeviceIds
   {
      public string device_id { get; set; }
      public ApplicationIds application_ids { get; set; }
   }

   public class DecodedPayload
   {
      public double temperature { get; set; }
      public double luminosity { get; set; }
   }

   public class Downlink
   {
      public int f_port { get; set; }
      public string frm_payload { get; set; }
      public DecodedPayload decoded_payload { get; set; }
      public string priority { get; set; }
      public bool confirmed { get; set; }
      public List<string> correlation_ids { get; set; }
   }

   public class Root
   {
      public EndDeviceIds end_device_ids { get; set; }
      public List<Downlink> downlinks { get; set; }
   }


}