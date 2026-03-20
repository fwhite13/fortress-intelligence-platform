/** Upload a local output file to S3 and return a pre-signed download URL. */
export declare function uploadOutputToS3(localPath: string, taskId: string, fileName: string): Promise<string>;
/** Upload input files (from multer) to S3 and clean up temp files. */
export declare function uploadInputsToS3(files: Express.Multer.File[], taskId: string): Promise<void>;
/** Download all input files from S3 to the task working directory. */
export declare function downloadInputsFromS3(taskId: string, workingDir: string): Promise<void>;
