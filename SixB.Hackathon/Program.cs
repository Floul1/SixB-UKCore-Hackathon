// See https://aka.ms/new-console-template for more information

using Hl7.Fhir.Model;
using SixB.Hackathon;

Console.WriteLine("Hello, World!");

var service = new ObservationService();
// await service.CreateObservation("RX7", "456", "789", "9234234599", 1.2m);
var newService = new IntakeOuttakeService();
var eocId = await newService.AdmitPatientToVirtualWard("9234234599");
await newService.DischargePatient("9234234599", eocId);
