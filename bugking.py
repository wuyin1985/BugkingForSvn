import subprocess
import datetime
import sys

ignore_authors = {'mlsvn_builder': True}

def get_logs(lines):
    ret = []
    end_line = len(lines) - 1
    for i in range(end_line - 1, -1, -1):
        line = lines[i]
        if line == "------------------------------------------------------------------------":
            ret.append(lines[i+1:end_line])
            end_line = i
    return ret

def get_last_commit_between_a_and_b_since_date(url, specified_date_str, a_hour, b_hour):
    try:
        # 将用户指定的日期字符串转换为日期对象
        specified_date = datetime.datetime.strptime(specified_date_str, "%Y-%m-%d").date()
    except ValueError as e:
        print(f"Invalid date format. Please use YYYY-MM-DD. Error: {e}")
        return

    # 获取当前日期
    today = datetime.date.today()

    # 如果指定日期是未来日期，则提示错误并返回
    if specified_date > today:
        print("Specified date is in the future. Please provide a valid past date.")
        return

    result_list = []
    while specified_date <= today:
        # 设置开始时间和结束时间为指定日期的a点和b点
        start_time = datetime.datetime.combine(specified_date, datetime.time(a_hour, 0, 0))
        end_time = datetime.datetime.combine(specified_date, datetime.time(b_hour, 0, 0))

        # 执行svn log命令，并获取输出
        svn_log_command = f"svn log {url} -r {{{start_time.isoformat()}}}:{{{end_time.isoformat()}}}"
        result = subprocess.run(svn_log_command, shell=True, capture_output=True, text=True)

        if result.returncode!= 0:
            print(f"Error running svn log command: {result.stderr}")
            return

        ret = result.stdout.strip()
        # 解析输出获取最后一个提交的信息
        log_entries = ret.split("\n")
        if log_entries:
            logs = get_logs(log_entries)
            for lines in logs:
                author_line = lines[0]
                author_infos = author_line.split("|")
                author = author_infos[1].strip()
                date = author_infos[2].strip()
                message_line = lines[2]

                if author not in ignore_authors:
                    result_list.append({
                        "date": date,
                        "message": message_line,
                        "author": author
                    })

        # 如果当天指定时间段内没有提交，将指定日期增加一天，继续检查下一天
        specified_date += datetime.timedelta(days=1)

    return result_list


if __name__ == "__main__":
    if len(sys.argv) < 5:
        print("Please provide a date in the format YYYY-MM-DD, a hour value and a b hour value as command line arguments. " + str(len(sys.argv)))
        sys.exit(1)

    url = sys.argv[1]
    specified_date_str = sys.argv[2]
    a_hour = int(sys.argv[3])
    b_hour = int(sys.argv[4])

    rank_mode = False
    if len(sys.argv) >= 6:
        rank_mode = (sys.argv[5] == "rank")
    

    result_list = get_last_commit_between_a_and_b_since_date(url, specified_date_str, a_hour, b_hour)
    if result_list:
        if rank_mode:
            counter = dict()
            for item in result_list:
                author = item["author"]
                count = counter.get(author, 0)
                count = count + 1
                counter[author] = count
            sorted_dict = dict(sorted(counter.items(), key=lambda item: item[1]))
            for key, value in sorted_dict.items():
                print(f"{key}:{value}")
        else:
            print("List of last commits between {}:00 and {}:00 since {}:".format(a_hour, b_hour, specified_date_str))
            for item in result_list:
                print("Date: {}".format(item["date"]))
                print("Message: {}".format(item["message"]))
                print("Author: {}".format(item["author"]))
                print("-" * 30)
    else:
        print("No commits found between the specified hours since the specified date.")