export interface Profile {
  username: string;
  email: string;
  fullName: string;
  clinicName?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  clinicLogo?: string;
  directoryEnabled?: boolean;
  onlineConsultations?: boolean;
  directorySportsTraining?: boolean;
  directoryCity?: string;
  directoryProvince?: string;
  directoryBio?: string;
  directorySpecialties?: string;
  directorySlug?: string;
  timeZoneId?: string;
}

export interface UpdateProfile {
  fullName: string;
  clinicName?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  clinicLogo?: string;
  directoryEnabled?: boolean;
  onlineConsultations?: boolean;
  directorySportsTraining?: boolean;
  directoryCity?: string;
  directoryProvince?: string;
  directoryBio?: string;
  directorySpecialties?: string;
  timeZoneId?: string;
}
