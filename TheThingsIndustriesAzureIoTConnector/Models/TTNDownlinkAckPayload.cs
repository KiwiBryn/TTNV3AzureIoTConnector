//---------------------------------------------------------------------------------
// Copyright (c) February 2021, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models
{
   using System;
   using System.Collections.Generic;

   using Newtonsoft.Json;
   using Newtonsoft.Json.Converters;
   using Newtonsoft.Json.Linq;

   public class DownlinkAck
   {
      [JsonProperty("session_key_id")]
      public string SessionKeyId { get; set; }

      [JsonProperty("f_port")]
      public int Port { get; set; }

      [JsonProperty("f_cnt")]
      public int Count { get; set; }

      [JsonProperty("frm_payload")]
      public string PayloadRaw { get; set; }

      [JsonProperty("decoded_payload")]
      public JToken PayloadDecoded { get; set; }

      [JsonProperty("confirmed")]
      public bool Confirmed { get; set; }

      [JsonProperty("priority")]
      [JsonConverter(typeof(StringEnumConverter))]
      public DownlinkPriority Priority { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }
   }

   public class DownlinkAckPayload
   {
      [JsonProperty("end_device_ids")]
      public EndDeviceIds EndDeviceIds { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }

      [JsonProperty("received_at")]
      public DateTime ReceivedAtUtc { get; set; }

      [JsonProperty("downlink_ack")]
      public DownlinkAck DownlinkAck { get; set; }
   }
}
