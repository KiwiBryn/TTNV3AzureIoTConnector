using System;
using System.Collections.Generic;

namespace devMobile.TheThingsNetwork.Models.DownlinkFailedRaw
{
   // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
   public class ApplicationIds
   {
      public string application_id { get; set; }
   }

   public class EndDeviceIds
   {
      public string device_id { get; set; }
      public ApplicationIds application_ids { get; set; }
      public string dev_eui { get; set; }
      public string join_eui { get; set; }
      public string dev_addr { get; set; }
   }

   public class DownlinkAck
   {
      public string session_key_id { get; set; }
      public int f_port { get; set; }
      public int f_cnt { get; set; }
      public string frm_payload { get; set; }
      public bool confirmed { get; set; }
      public string priority { get; set; }
      public List<string> correlation_ids { get; set; }
   }

   public class Root
   {
      public EndDeviceIds end_device_ids { get; set; }
      public List<string> correlation_ids { get; set; }
      public DateTime received_at { get; set; }
      public DownlinkAck downlink_ack { get; set; }
   }
}