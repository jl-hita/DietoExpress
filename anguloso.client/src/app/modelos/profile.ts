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
  directoryCity?: string;
  directoryProvince?: string;
  directoryBio?: string;
  directorySpecialties?: string;
  directorySlug?: string;
}

export interface UpdateProfile {
  fullName: string;
  clinicName?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  clinicLogo?: string;
  directoryEnabled?: boolean;
  onlineConsultations?: boolean;
  directoryCity?: string;
  directoryProvince?: string;
  directoryBio?: string;
  directorySpecialties?: string;
}
