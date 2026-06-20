@echo off
"C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Unity.exe" -runTests -batchmode -projectPath "D:\barcade\Barcade" -testPlatform EditMode -testResults "D:\barcade\Barcade\TestResults\t006-edit-v2.xml" -logFile "D:\barcade\Barcade\TestResults\t006-edit-v2.log"
echo EditMode exit code: %ERRORLEVEL%
"C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Unity.exe" -runTests -batchmode -projectPath "D:\barcade\Barcade" -testPlatform PlayMode -testResults "D:\barcade\Barcade\TestResults\t006-play-v2.xml" -logFile "D:\barcade\Barcade\TestResults\t006-play-v2.log"
echo PlayMode exit code: %ERRORLEVEL%
